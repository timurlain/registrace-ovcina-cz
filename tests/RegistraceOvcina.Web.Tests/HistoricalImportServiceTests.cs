using System.ComponentModel.DataAnnotations;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RegistraceOvcina.Web.Components.Pages.Admin;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.HistoricalImport;
using RegistraceOvcina.Web.Security;

namespace RegistraceOvcina.Web.Tests;

public sealed class HistoricalImportServiceTests
{
    [Fact]
    public void HistoricalImportPage_RequiresAdminPolicy()
    {
        var authorizeAttribute = typeof(HistoricalImport)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();

        Assert.Equal(AuthorizationPolicies.AdminOnly, authorizeAttribute.Policy);
    }

    [Fact]
    public async Task ImportWorkbookAsync_ImportsGoogleFormWorkbookIntoHistoricalGame()
    {
        var options = CreateOptions();
        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(CreateUser("admin-id", "admin@example.cz"));
            await db.SaveChangesAsync();
        }

        var service = new HistoricalImportService(new TestDbContextFactory(options), new FixedTimeProvider());
        await using var workbookStream = BuildGoogleFormWorkbook();

        var result = await service.ImportWorkbookAsync(
            new HistoricalImportCommand(
                "Historie 2025",
                "29. Ovčina S jídlem roste chuť",
                new DateTime(2025, 5, 3, 8, 0, 0, DateTimeKind.Unspecified),
                new DateTime(2025, 5, 4, 16, 0, 0, DateTimeKind.Unspecified),
                "OrganizaceLidi.xlsx"),
            workbookStream,
            "admin-id");

        await using var verificationDb = new ApplicationDbContext(options);
        var batch = await verificationDb.HistoricalImportBatches
            .Include(x => x.Game)
            .SingleAsync(x => x.Id == result.BatchId);

        Assert.Equal("Historie 2025", batch.Label);
        Assert.Equal("29. Ovčina S jídlem roste chuť", batch.Game.Name);
        Assert.False(batch.Game.IsPublished);
        Assert.Equal(2, batch.TotalSourceRows);
        Assert.Equal(1, batch.HouseholdCount);
        Assert.Equal(2, batch.RegistrationCount);
        Assert.Equal(2, batch.PersonCreatedCount);
        Assert.Equal(0, batch.WarningCount);

        Assert.Equal(2, await verificationDb.People.CountAsync());
        Assert.Equal(1, await verificationDb.RegistrationSubmissions.CountAsync());
        Assert.Equal(2, await verificationDb.Registrations.CountAsync());
        Assert.Equal(1, await verificationDb.Characters.CountAsync());
        Assert.Equal(2, await verificationDb.Kingdoms.CountAsync());
        Assert.Equal(2, await verificationDb.HistoricalImportRows.CountAsync());

        var appearance = await verificationDb.CharacterAppearances
            .Include(x => x.AssignedKingdom)
            .SingleAsync();

        Assert.Equal("Nový Arnor", appearance.AssignedKingdom?.DisplayName);
    }

    [Fact]
    public async Task ImportWorkbookAsync_RerunStaysSafeForLegacyWorkbook()
    {
        var options = CreateOptions();
        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(CreateUser("admin-id", "admin@example.cz"));
            await db.SaveChangesAsync();
        }

        var service = new HistoricalImportService(new TestDbContextFactory(options), new FixedTimeProvider());

        await using (var firstRun = BuildLegacyWorkbook())
        {
            await service.ImportWorkbookAsync(
                new HistoricalImportCommand(
                    "Historie 2024",
                    "28. Ovčina Za pokladem ze Severní spouště",
                    new DateTime(2024, 5, 4, 8, 0, 0, DateTimeKind.Unspecified),
                    new DateTime(2024, 5, 5, 16, 0, 0, DateTimeKind.Unspecified),
                    "OrganiZaceLidi.xlsx"),
                firstRun,
                "admin-id");
        }

        HistoricalImportResult secondResult;
        await using (var secondRun = BuildLegacyWorkbook())
        {
            secondResult = await service.ImportWorkbookAsync(
                new HistoricalImportCommand(
                    "Historie 2024 opravná dávka",
                    "28. Ovčina Za pokladem ze Severní spouště",
                    new DateTime(2024, 5, 4, 8, 0, 0, DateTimeKind.Unspecified),
                    new DateTime(2024, 5, 5, 16, 0, 0, DateTimeKind.Unspecified),
                    "OrganiZaceLidi.xlsx"),
                secondRun,
                "admin-id");
        }

        await using var verificationDb = new ApplicationDbContext(options);
        Assert.Equal(2, await verificationDb.HistoricalImportBatches.CountAsync());
        Assert.Equal(3, await verificationDb.HistoricalImportRows.CountAsync());
        Assert.Equal(2, await verificationDb.People.CountAsync());
        Assert.Equal(1, await verificationDb.RegistrationSubmissions.CountAsync());
        Assert.Equal(2, await verificationDb.Registrations.CountAsync());

        var secondBatch = await verificationDb.HistoricalImportBatches.SingleAsync(x => x.Id == secondResult.BatchId);
        Assert.Equal(0, secondBatch.PersonCreatedCount);
        Assert.Equal(3, secondBatch.PersonMatchedCount);
        Assert.Equal(1, secondBatch.HouseholdCount);
        Assert.Equal(2, secondBatch.RegistrationCount);

        var jarcaRegistration = await verificationDb.Registrations
            .Include(x => x.Person)
            .Include(x => x.PreferredKingdom)
            .SingleAsync(x => x.Person.FirstName == "Jarča");

        Assert.Equal(AttendeeType.Adult, jarcaRegistration.AttendeeType);
        Assert.True(jarcaRegistration.AdultRoles.HasFlag(AdultRoleFlags.RangerLeader));
        Assert.Equal("Elfové", jarcaRegistration.PreferredKingdom?.DisplayName);
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static ApplicationUser CreateUser(string id, string email) =>
        new()
        {
            Id = id,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            DisplayName = "Správce",
            EmailConfirmed = true,
            IsActive = true,
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N")
        };

    private static MemoryStream BuildGoogleFormWorkbook()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Registrační formulář");
        var headers = new[]
        {
            "Časová značka",
            "Název skupiny",
            "Jméno a Příjmení",
            "Jméno hrané postavy.",
            "Věk účastníka",
            "Typ účastníka",
            "Osobní quest",
            "Představa osobního questu",
            "Jídlo sobota",
            "Jídlo neděle",
            "Zákonný zástupce",
            "Poznámka",
            "Kontaktní email",
            "Kontaktní telefon",
            "Hráč",
            "NPC",
            "Příšera",
            "Technická pomoc",
            "Národ",
            "Osobní quest vytvořený",
            "Link na quest"
        };

        for (var index = 0; index < headers.Length; index++)
        {
            sheet.Cell(1, index + 1).Value = headers[index];
        }

        sheet.Cell(2, 1).Value = "2025-03-31 11:17:46";
        sheet.Cell(2, 2).Value = "Kopečci";
        sheet.Cell(2, 3).Value = "Tomáš Kopecký";
        sheet.Cell(2, 4).Value = "Tomas";
        sheet.Cell(2, 5).Value = "15";
        sheet.Cell(2, 6).Value = "hrající samostatné dítě (cca 10+ , které chce naplno soutěžit s dalšími - PVP hráč)";
        sheet.Cell(2, 11).Value = "Magda Kopecká";
        sheet.Cell(2, 12).Value = "Nový Arnor";
        sheet.Cell(2, 13).Value = "magda.kopecka@email.cz";
        sheet.Cell(2, 14).Value = "603356214";
        sheet.Cell(2, 15).Value = true;
        sheet.Cell(2, 19).Value = "Nový Arnor";

        sheet.Cell(3, 1).Value = "2025-03-31 11:02:44";
        sheet.Cell(3, 2).Value = "Kopečci";
        sheet.Cell(3, 3).Value = "Magda Kopecká";
        sheet.Cell(3, 5).Value = "45";
        sheet.Cell(3, 6).Value = "dospělý se zájmem hrát cizí postavu - příšeru (např. skřet, kostlivec, vlci.)";
        sheet.Cell(3, 13).Value = "magda.kopecka@email.cz";
        sheet.Cell(3, 14).Value = "603356214";
        sheet.Cell(3, 17).Value = true;
        sheet.Cell(3, 19).Value = "Příšera";

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildLegacyWorkbook()
    {
        using var workbook = new XLWorkbook();
        var deti = workbook.AddWorksheet("Deti");
        deti.Cell(1, 1).Value = "Název skupiny";
        deti.Cell(1, 2).Value = "Jméno a Příjmení";
        deti.Cell(1, 3).Value = "Věk účastníka";
        deti.Cell(1, 4).Value = "Typ účastníka";
        deti.Cell(1, 5).Value = "Jméno zákonného zástupce (vyplňte jen pro děti a nesvéprávné)";
        deti.Cell(1, 6).Value = "Poznámka pro organizátory";
        deti.Cell(1, 7).Value = "Kontaktní email";
        deti.Cell(1, 8).Value = "Kontaktní telefon";

        deti.Cell(2, 1).Value = "Tylečkovi";
        deti.Cell(2, 2).Value = "Jarča Tylečková";
        deti.Cell(2, 3).Value = "50";
        deti.Cell(2, 4).Value = "dospělý se zájmem vést skupinku menších dětí (hraničář)";
        deti.Cell(2, 6).Value = "Prosím o elfy";
        deti.Cell(2, 7).Value = "carda.jarca@post.cz";
        deti.Cell(2, 8).Value = "775289789";

        deti.Cell(3, 1).Value = "Tylečkovi";
        deti.Cell(3, 2).Value = "Toník Tyleček";
        deti.Cell(3, 3).Value = "9";
        deti.Cell(3, 4).Value = "hrající dítě ve skupince s hraničářem";
        deti.Cell(3, 5).Value = "Jarča Tylečková";
        deti.Cell(3, 7).Value = "carda.jarca@post.cz";
        deti.Cell(3, 8).Value = "775289789";

        var orgove = workbook.AddWorksheet("Orgove");
        orgove.Cell(1, 1).Value = "Jméno a Příjmení";
        orgove.Cell(1, 2).Value = "Věk účastníka";
        orgove.Cell(1, 3).Value = "Role";
        orgove.Cell(1, 4).Value = "Poznámka pro organizátory";
        orgove.Cell(1, 5).Value = "Kontaktní email";
        orgove.Cell(1, 6).Value = "Kontaktní telefon";

        orgove.Cell(2, 1).Value = "Jarča Tylečková";
        orgove.Cell(2, 2).Value = "50";
        orgove.Cell(2, 3).Value = "Elfové - Hraničář";
        orgove.Cell(2, 4).Value = "Prosím tuto skupinu nerozdělovat.";
        orgove.Cell(2, 5).Value = "carda.jarca@post.cz";
        orgove.Cell(2, 6).Value = "775289789";

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ApplicationDbContext(options));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now = new(new DateTime(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc));

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
