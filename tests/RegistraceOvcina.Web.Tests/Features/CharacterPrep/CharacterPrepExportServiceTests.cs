using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.CharacterPrep;

namespace RegistraceOvcina.Web.Tests.Features.CharacterPrep;

public sealed class CharacterPrepExportServiceTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
    private const int GameId = 1;
    private const int EquipmentId = 10;

    [Fact]
    public async Task BuildAsync_returns_non_empty_bytes()
    {
        var options = CreateOptions();
        await SeedAsync(options);

        var service = new CharacterPrepExportService(new TestDbContextFactory(options));

        var bytes = await service.BuildAsync(GameId, CancellationToken.None);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task BuildAsync_returns_valid_xlsx()
    {
        var options = CreateOptions();
        await SeedAsync(options);

        var service = new CharacterPrepExportService(new TestDbContextFactory(options));

        var bytes = await service.BuildAsync(GameId, CancellationToken.None);

        // Roundtrip open — should not throw.
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        Assert.True(workbook.Worksheets.Any());
    }

    [Fact]
    public async Task BuildAsync_header_row_has_7_columns_in_correct_order()
    {
        var options = CreateOptions();
        await SeedAsync(options);

        var service = new CharacterPrepExportService(new TestDbContextFactory(options));

        var bytes = await service.BuildAsync(GameId, CancellationToken.None);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Příprava postav");

        Assert.Equal("Hráč", sheet.Cell(1, 1).GetString());
        Assert.Equal("Rok narození", sheet.Cell(1, 2).GetString());
        Assert.Equal("Jméno postavy", sheet.Cell(1, 3).GetString());
        Assert.Equal("Startovní výbava", sheet.Cell(1, 4).GetString());
        Assert.Equal("Poznámka", sheet.Cell(1, 5).GetString());
        Assert.Equal("Domácnost", sheet.Cell(1, 6).GetString());
        Assert.Equal("Email domácnosti", sheet.Cell(1, 7).GetString());
        Assert.Equal("", sheet.Cell(1, 8).GetString());
        Assert.True(sheet.Cell(1, 1).Style.Font.Bold);
    }

    [Fact]
    public async Task BuildAsync_row_count_matches_Player_attendee_count()
    {
        var options = CreateOptions();
        await SeedAsync(options);

        var service = new CharacterPrepExportService(new TestDbContextFactory(options));

        var bytes = await service.BuildAsync(GameId, CancellationToken.None);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Příprava postav");

        // 3 Player registrations seeded → rows 2, 3, 4. Row 5 should be empty.
        Assert.NotEqual("", sheet.Cell(2, 1).GetString());
        Assert.NotEqual("", sheet.Cell(3, 1).GetString());
        Assert.NotEqual("", sheet.Cell(4, 1).GetString());
        Assert.Equal("", sheet.Cell(5, 1).GetString());
    }

    [Fact]
    public async Task BuildAsync_excludes_non_Player_attendees()
    {
        var options = CreateOptions();
        await SeedAsync(options);

        var service = new CharacterPrepExportService(new TestDbContextFactory(options));

        var bytes = await service.BuildAsync(GameId, CancellationToken.None);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Příprava postav");

        // The Adult helper (seeded with name "Dospělák Pomocník") must not appear.
        var allValues = Enumerable.Range(2, 10)
            .Select(r => sheet.Cell(r, 1).GetString())
            .ToList();
        Assert.DoesNotContain(allValues, v => v.Contains("Dospělák", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildAsync_empty_cells_stay_empty_not_placeholder()
    {
        var options = CreateOptions();
        await SeedAsync(options);

        var service = new CharacterPrepExportService(new TestDbContextFactory(options));

        var bytes = await service.BuildAsync(GameId, CancellationToken.None);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Příprava postav");

        // Anna Adamová (row 2) has no character name, equipment, or note.
        Assert.Equal("Anna Adamová", sheet.Cell(2, 1).GetString());
        Assert.Equal("", sheet.Cell(2, 3).GetString()); // Jméno postavy
        Assert.Equal("", sheet.Cell(2, 4).GetString()); // Startovní výbava
        Assert.Equal("", sheet.Cell(2, 5).GetString()); // Poznámka
    }

    [Fact]
    public async Task BuildAsync_Czech_characters_encode_correctly()
    {
        var options = CreateOptions();
        await SeedAsync(options);

        var service = new CharacterPrepExportService(new TestDbContextFactory(options));

        var bytes = await service.BuildAsync(GameId, CancellationToken.None);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Příprava postav");

        // Šárka Černá was seeded with diacritics intact; bytes must roundtrip exactly.
        var found = false;
        for (var r = 2; r <= 10; r++)
        {
            if (sheet.Cell(r, 1).GetString() == "Šárka Černá")
            {
                found = true;
                break;
            }
        }
        Assert.True(found, "Expected a row with name 'Šárka Černá' (diacritics preserved).");
    }

    [Fact]
    public async Task BuildAsync_orders_by_lastname_firstname()
    {
        var options = CreateOptions();
        await SeedAsync(options);

        var service = new CharacterPrepExportService(new TestDbContextFactory(options));

        var bytes = await service.BuildAsync(GameId, CancellationToken.None);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Příprava postav");

        // Players sorted by LastName, then FirstName:
        //   Anna Adamová (Adamová, Anna)
        //   Bořek Adamů (Adamů, Bořek)
        //   Šárka Černá (Černá, Šárka)
        Assert.Equal("Anna Adamová", sheet.Cell(2, 1).GetString());
        Assert.Equal("Bořek Adamů", sheet.Cell(3, 1).GetString());
        Assert.Equal("Šárka Černá", sheet.Cell(4, 1).GetString());
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static async Task SeedAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);

        db.Games.Add(new Game
        {
            Id = GameId,
            Name = "Ovčina 2026",
            StartsAtUtc = FixedUtc.AddDays(30),
            EndsAtUtc = FixedUtc.AddDays(32),
            RegistrationClosesAtUtc = FixedUtc.AddDays(20),
            MealOrderingClosesAtUtc = FixedUtc.AddDays(15),
            PaymentDueAtUtc = FixedUtc.AddDays(25),
            PlayerBasePrice = 1200,
            AdultHelperBasePrice = 800,
            BankAccount = "x",
            BankAccountName = "y",
            VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
            IsPublished = true,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc,
        });

        db.StartingEquipmentOptions.Add(new StartingEquipmentOption
        {
            Id = EquipmentId,
            GameId = GameId,
            Key = "sword",
            DisplayName = "Meč (3/1)",
            SortOrder = 1,
        });

        db.Users.Add(new ApplicationUser
        {
            Id = "user-1",
            DisplayName = "Parent 1",
            Email = "p1@example.cz",
            NormalizedEmail = "P1@EXAMPLE.CZ",
            UserName = "p1@example.cz",
            NormalizedUserName = "P1@EXAMPLE.CZ",
            EmailConfirmed = true,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = FixedUtc,
        });
        db.Users.Add(new ApplicationUser
        {
            Id = "user-2",
            DisplayName = "Parent 2",
            Email = "p2@example.cz",
            NormalizedEmail = "P2@EXAMPLE.CZ",
            UserName = "p2@example.cz",
            NormalizedUserName = "P2@EXAMPLE.CZ",
            EmailConfirmed = true,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = FixedUtc,
        });

        db.RegistrationSubmissions.Add(new RegistrationSubmission
        {
            Id = 1,
            GameId = GameId,
            RegistrantUserId = "user-1",
            PrimaryContactName = "Adamovi",
            PrimaryEmail = "adamovi@example.cz",
            PrimaryPhone = "777111222",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200,
        });
        db.RegistrationSubmissions.Add(new RegistrationSubmission
        {
            Id = 2,
            GameId = GameId,
            RegistrantUserId = "user-2",
            PrimaryContactName = "Černí",
            PrimaryEmail = "cerni@example.cz",
            PrimaryPhone = "777333444",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200,
        });

        db.People.Add(MakePerson(101, "Anna", "Adamová", 2015));
        db.People.Add(MakePerson(102, "Bořek", "Adamů", 2014));
        db.People.Add(MakePerson(103, "Dospělák", "Pomocník", 1990));
        db.People.Add(MakePerson(201, "Šárka", "Černá", 2013));

        // Submission 1: Anna (no prep), Bořek (with prep), adult helper (excluded).
        db.Registrations.Add(new Registration
        {
            Id = 1001,
            SubmissionId = 1,
            PersonId = 101,
            AttendeeType = AttendeeType.Player,
            Status = RegistrationStatus.Active,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc,
        });
        db.Registrations.Add(new Registration
        {
            Id = 1002,
            SubmissionId = 1,
            PersonId = 102,
            AttendeeType = AttendeeType.Player,
            Status = RegistrationStatus.Active,
            CharacterName = "Elrond",
            StartingEquipmentOptionId = EquipmentId,
            CharacterPrepNote = "preferuje luk",
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc,
        });
        db.Registrations.Add(new Registration
        {
            Id = 1003,
            SubmissionId = 1,
            PersonId = 103,
            AttendeeType = AttendeeType.Adult,
            Status = RegistrationStatus.Active,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc,
        });

        // Submission 2: Šárka Černá (Player, Czech diacritics).
        db.Registrations.Add(new Registration
        {
            Id = 2001,
            SubmissionId = 2,
            PersonId = 201,
            AttendeeType = AttendeeType.Player,
            Status = RegistrationStatus.Active,
            CharacterName = "Galadriel",
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc,
        });

        await db.SaveChangesAsync();
    }

    private static Person MakePerson(int id, string first, string last, int birthYear) =>
        new()
        {
            Id = id,
            FirstName = first,
            LastName = last,
            BirthYear = birthYear,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc,
        };

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ApplicationDbContext(options));
    }
}
