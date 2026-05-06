using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Feedback;

namespace RegistraceOvcina.Web.Tests.Features.Feedback;

/// <summary>
/// In-memory tests for <see cref="FeedbackExportService"/>. Mirrors the
/// fixture style used by <c>FeedbackDashboardTests</c>: a private
/// <see cref="TestDbContextFactory"/>, per-test in-memory DB, and a small
/// set of seed helpers for games / submissions / registrations.
/// </summary>
public sealed class FeedbackExportServiceTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset NowUtc = new(FixedUtc.AddDays(40), TimeSpan.Zero);

    private const int GameId = 1;

    private const string KidJson =
        """[{"key":"co_libilo","label":"Co se ti líbilo?"},{"key":"co_kazilo","label":"Co tě naopak štvalo?"}]""";

    private const string AdultJson =
        """[{"key":"organizace","label":"Jak hodnotíš organizaci?"}]""";

    [Fact]
    public async Task ExportAsync_returns_workbook_with_two_sheets()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, GameId);
        var sut = CreateSut(options);

        var bytes = await sut.ExportAsync(GameId, CancellationToken.None);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        Assert.Equal(2, workbook.Worksheets.Count());
        Assert.NotNull(workbook.Worksheet("Děti"));
        Assert.NotNull(workbook.Worksheet("Dospělí"));
    }

    [Fact]
    public async Task ExportAsync_kid_sheet_has_kid_question_columns()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, GameId);
        var sut = CreateSut(options);

        var bytes = await sut.ExportAsync(GameId, CancellationToken.None);

        var sheet = OpenSheet(bytes, "Děti");
        Assert.Equal("Jméno", sheet.Cell(1, 1).GetString());
        Assert.Equal("Příjmení", sheet.Cell(1, 2).GetString());
        Assert.Equal("Rodina", sheet.Cell(1, 3).GetString());
        Assert.Equal("Postava", sheet.Cell(1, 4).GetString());
        Assert.Equal("Stav", sheet.Cell(1, 5).GetString());
        Assert.Equal("Odesláno", sheet.Cell(1, 6).GetString());
        Assert.Equal("Vyplnil", sheet.Cell(1, 7).GetString());
        Assert.Equal("Pin do kroniky", sheet.Cell(1, 8).GetString());
        Assert.Equal("Poznámka organizátora", sheet.Cell(1, 9).GetString());
        Assert.Equal("Co se ti líbilo?", sheet.Cell(1, 10).GetString());
        Assert.Equal("Co tě naopak štvalo?", sheet.Cell(1, 11).GetString());
        Assert.True(sheet.Cell(1, 1).Style.Font.Bold);
        Assert.True(sheet.Cell(1, 10).Style.Font.Bold);
    }

    [Fact]
    public async Task ExportAsync_adult_sheet_has_adult_question_columns()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, GameId);
        var sut = CreateSut(options);

        var bytes = await sut.ExportAsync(GameId, CancellationToken.None);

        var sheet = OpenSheet(bytes, "Dospělí");
        Assert.Equal("Jméno", sheet.Cell(1, 1).GetString());
        Assert.Equal("Jak hodnotíš organizaci?", sheet.Cell(1, 10).GetString());
        // Adult sheet must NOT have the kid question columns.
        Assert.NotEqual("Co se ti líbilo?", sheet.Cell(1, 11).GetString());
    }

    [Fact]
    public async Task ExportAsync_writes_one_row_per_registration_with_answers()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, GameId);

        await SeedRegistrationAsync(options,
            registrationId: 1, submissionId: 1, personId: 101,
            firstName: "Anna", lastName: "Adamová",
            attendeeType: AttendeeType.Player,
            invitedAtUtc: NowUtc.AddDays(-3),
            submittedAtUtc: NowUtc.AddDays(-1),
            characterName: "Galadriel",
            answers: new() { ["co_libilo"] = "Bitva u brodu", ["co_kazilo"] = "Bláto" });

        await SeedRegistrationAsync(options,
            registrationId: 2, submissionId: 1, personId: 102,
            firstName: "Bořek", lastName: "Adamů",
            attendeeType: AttendeeType.Player,
            invitedAtUtc: NowUtc.AddDays(-3));

        await SeedRegistrationAsync(options,
            registrationId: 3, submissionId: 1, personId: 103,
            firstName: "Karel", lastName: "Dospělý",
            attendeeType: AttendeeType.Adult,
            invitedAtUtc: NowUtc.AddDays(-3),
            submittedAtUtc: NowUtc.AddDays(-1),
            answers: new() { ["organizace"] = "Skvělá" });

        var sut = CreateSut(options);

        var bytes = await sut.ExportAsync(GameId, CancellationToken.None);

        var kid = OpenSheet(bytes, "Děti");
        // Two kids, sorted by lastname: Adamová, Adamů
        Assert.Equal("Anna", kid.Cell(2, 1).GetString());
        Assert.Equal("Adamová", kid.Cell(2, 2).GetString());
        Assert.Equal("Galadriel", kid.Cell(2, 4).GetString());
        Assert.Equal("Hotovo", kid.Cell(2, 5).GetString());
        Assert.Equal("Bitva u brodu", kid.Cell(2, 10).GetString());
        Assert.Equal("Bláto", kid.Cell(2, 11).GetString());

        Assert.Equal("Bořek", kid.Cell(3, 1).GetString());
        Assert.Equal("Adamů", kid.Cell(3, 2).GetString());
        Assert.Equal("Čeká", kid.Cell(3, 5).GetString());
        // No fourth kid.
        Assert.Equal("", kid.Cell(4, 1).GetString());

        var adult = OpenSheet(bytes, "Dospělí");
        Assert.Equal("Karel", adult.Cell(2, 1).GetString());
        Assert.Equal("Skvělá", adult.Cell(2, 10).GetString());
        // Only one adult.
        Assert.Equal("", adult.Cell(3, 1).GetString());
    }

    [Fact]
    public async Task ExportAsync_omits_attendees_without_response_or_invitation()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, GameId);

        // Attendee with neither invitation nor response → must be omitted.
        await SeedRegistrationAsync(options,
            registrationId: 1, submissionId: 1, personId: 101,
            firstName: "Skip", lastName: "Me",
            attendeeType: AttendeeType.Player);

        // Attendee with an invitation but no response → included (Čeká).
        await SeedRegistrationAsync(options,
            registrationId: 2, submissionId: 1, personId: 102,
            firstName: "Keep", lastName: "Me",
            attendeeType: AttendeeType.Player,
            invitedAtUtc: NowUtc.AddDays(-3));

        var sut = CreateSut(options);

        var bytes = await sut.ExportAsync(GameId, CancellationToken.None);

        var kid = OpenSheet(bytes, "Děti");
        Assert.Equal("Keep", kid.Cell(2, 1).GetString());
        Assert.Equal("", kid.Cell(3, 1).GetString());
        // "Skip Me" (no invite, no response) must not appear anywhere.
        for (var r = 2; r <= 5; r++)
        {
            Assert.NotEqual("Skip", kid.Cell(r, 1).GetString());
        }
    }

    [Fact]
    public async Task ExportAsync_writes_empty_cell_for_missing_answer_key()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, GameId);

        // Only one of the two kid questions is answered.
        await SeedRegistrationAsync(options,
            registrationId: 1, submissionId: 1, personId: 101,
            firstName: "Anna", lastName: "Adamová",
            attendeeType: AttendeeType.Player,
            invitedAtUtc: NowUtc.AddDays(-3),
            submittedAtUtc: NowUtc.AddDays(-1),
            answers: new() { ["co_libilo"] = "Něco" });
        var sut = CreateSut(options);

        var bytes = await sut.ExportAsync(GameId, CancellationToken.None);

        var kid = OpenSheet(bytes, "Děti");
        Assert.Equal("Něco", kid.Cell(2, 10).GetString());
        // co_kazilo not in dict → cell must be empty (no placeholder).
        Assert.Equal("", kid.Cell(2, 11).GetString());
    }

    [Fact]
    public async Task ExportAsync_returns_headers_only_when_no_data()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, GameId);
        var sut = CreateSut(options);

        var bytes = await sut.ExportAsync(GameId, CancellationToken.None);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        Assert.Equal(2, workbook.Worksheets.Count());
        var kid = workbook.Worksheet("Děti");
        Assert.Equal("Jméno", kid.Cell(1, 1).GetString());
        Assert.Equal("", kid.Cell(2, 1).GetString());
        var adult = workbook.Worksheet("Dospělí");
        Assert.Equal("Jméno", adult.Cell(1, 1).GetString());
        Assert.Equal("", adult.Cell(2, 1).GetString());
    }

    [Fact]
    public async Task ExportAsync_preserves_czech_diacritics_in_answers()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, GameId);

        await SeedRegistrationAsync(options,
            registrationId: 1, submissionId: 1, personId: 101,
            firstName: "Šárka", lastName: "Černá",
            attendeeType: AttendeeType.Player,
            invitedAtUtc: NowUtc.AddDays(-3),
            submittedAtUtc: NowUtc.AddDays(-1),
            answers: new()
            {
                ["co_libilo"] = "Příšerně řvoucí ďábel u řeky — žluťoučký kůň úpěl",
            });
        var sut = CreateSut(options);

        var bytes = await sut.ExportAsync(GameId, CancellationToken.None);

        var kid = OpenSheet(bytes, "Děti");
        Assert.Equal("Šárka", kid.Cell(2, 1).GetString());
        Assert.Equal("Černá", kid.Cell(2, 2).GetString());
        Assert.Equal(
            "Příšerně řvoucí ďábel u řeky — žluťoučký kůň úpěl",
            kid.Cell(2, 10).GetString());
    }

    [Fact]
    public async Task ExportAsync_excludes_soft_deleted_submissions()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, GameId);
        await SeedSubmissionAsync(options, submissionId: 99, gameId: GameId, isDeleted: true);

        await SeedRegistrationAsync(options,
            registrationId: 1, submissionId: 1, personId: 101,
            firstName: "Visible", lastName: "Player",
            attendeeType: AttendeeType.Player,
            invitedAtUtc: NowUtc.AddDays(-3),
            submittedAtUtc: NowUtc.AddDays(-1));

        await SeedRegistrationAsync(options,
            registrationId: 2, submissionId: 99, personId: 102,
            firstName: "Hidden", lastName: "Player",
            attendeeType: AttendeeType.Player,
            invitedAtUtc: NowUtc.AddDays(-3),
            submittedAtUtc: NowUtc.AddDays(-1));

        var sut = CreateSut(options);

        var bytes = await sut.ExportAsync(GameId, CancellationToken.None);

        var kid = OpenSheet(bytes, "Děti");
        Assert.Equal("Visible", kid.Cell(2, 1).GetString());
        // Hidden one (under soft-deleted submission) must not appear.
        for (var r = 2; r <= 5; r++)
        {
            Assert.NotEqual("Hidden", kid.Cell(r, 1).GetString());
        }
    }

    [Fact]
    public async Task ExportAsync_excludes_other_games()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, GameId);
        await SeedGameAsync(options, gameId: 2);

        await SeedRegistrationAsync(options,
            registrationId: 1, submissionId: 1, personId: 101,
            firstName: "Hra1", lastName: "Player",
            attendeeType: AttendeeType.Player,
            invitedAtUtc: NowUtc.AddDays(-3),
            submittedAtUtc: NowUtc.AddDays(-1));

        await SeedRegistrationAsync(options,
            registrationId: 2, submissionId: 2, personId: 102,
            firstName: "Hra2", lastName: "Player",
            attendeeType: AttendeeType.Player,
            invitedAtUtc: NowUtc.AddDays(-3),
            submittedAtUtc: NowUtc.AddDays(-1));

        var sut = CreateSut(options);

        var bytes = await sut.ExportAsync(GameId, CancellationToken.None);

        var kid = OpenSheet(bytes, "Děti");
        Assert.Equal("Hra1", kid.Cell(2, 1).GetString());
        for (var r = 2; r <= 5; r++)
        {
            Assert.NotEqual("Hra2", kid.Cell(r, 1).GetString());
        }
    }

    [Fact]
    public async Task ExportAsync_writes_pin_to_chronicle_and_organizer_note()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, GameId);

        await SeedRegistrationAsync(options,
            registrationId: 1, submissionId: 1, personId: 101,
            firstName: "Anna", lastName: "Adamová",
            attendeeType: AttendeeType.Player,
            invitedAtUtc: NowUtc.AddDays(-3),
            submittedAtUtc: NowUtc.AddDays(-1),
            pinToChronicle: true,
            organizerNote: "Připnout do kroniky");

        var sut = CreateSut(options);

        var bytes = await sut.ExportAsync(GameId, CancellationToken.None);

        var kid = OpenSheet(bytes, "Děti");
        Assert.Equal("Ano", kid.Cell(2, 8).GetString());
        Assert.Equal("Připnout do kroniky", kid.Cell(2, 9).GetString());
    }

    // -------------------------------------------------------------------------
    // helpers
    // -------------------------------------------------------------------------

    private static IXLWorksheet OpenSheet(byte[] bytes, string sheetName)
    {
        var stream = new MemoryStream(bytes);
        var workbook = new XLWorkbook(stream);
        return workbook.Worksheet(sheetName);
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static FeedbackExportService CreateSut(DbContextOptions<ApplicationDbContext> options)
    {
        var factory = new TestDbContextFactory(options);
        return new FeedbackExportService(factory, new FeedbackOptionsService(factory));
    }

    private static async Task SeedGameAsync(
        DbContextOptions<ApplicationDbContext> options,
        int gameId)
    {
        await using var db = new ApplicationDbContext(options);

        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new ApplicationUser
            {
                Id = "user-1",
                Email = "parent@example.cz",
                NormalizedEmail = "PARENT@EXAMPLE.CZ",
                UserName = "parent@example.cz",
                NormalizedUserName = "PARENT@EXAMPLE.CZ",
                EmailConfirmed = true,
                IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = FixedUtc,
            });
        }

        db.Games.Add(new Game
        {
            Id = gameId,
            Name = $"Ovčina {2026 + gameId}",
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
            FeedbackKidQuestionsJson = KidJson,
            FeedbackAdultQuestionsJson = AdultJson,
        });

        // Default submission per game (id == gameId) — matches the default-
        // submission shape used by FeedbackDashboardTests.
        db.RegistrationSubmissions.Add(new RegistrationSubmission
        {
            Id = gameId,
            GameId = gameId,
            RegistrantUserId = "user-1",
            PrimaryContactName = $"Household {gameId}",
            PrimaryEmail = "parent@example.cz",
            PrimaryPhone = "777111222",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200,
        });

        await db.SaveChangesAsync();
    }

    private static async Task SeedSubmissionAsync(
        DbContextOptions<ApplicationDbContext> options,
        int submissionId,
        int gameId,
        bool isDeleted = false)
    {
        await using var db = new ApplicationDbContext(options);
        db.RegistrationSubmissions.Add(new RegistrationSubmission
        {
            Id = submissionId,
            GameId = gameId,
            RegistrantUserId = "user-1",
            PrimaryContactName = $"Household {submissionId}",
            PrimaryEmail = "parent@example.cz",
            PrimaryPhone = "777111222",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200,
            IsDeleted = isDeleted,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedRegistrationAsync(
        DbContextOptions<ApplicationDbContext> options,
        int registrationId,
        int submissionId,
        int personId,
        string firstName,
        string lastName,
        AttendeeType attendeeType,
        DateTimeOffset? invitedAtUtc = null,
        DateTimeOffset? submittedAtUtc = null,
        string? characterName = null,
        Dictionary<string, string>? answers = null,
        bool pinToChronicle = false,
        string? organizerNote = null)
    {
        await using var db = new ApplicationDbContext(options);
        db.People.Add(new Person
        {
            Id = personId,
            FirstName = firstName,
            LastName = lastName,
            BirthYear = attendeeType == AttendeeType.Player ? 2015 : 1985,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc,
        });
        db.Registrations.Add(new Registration
        {
            Id = registrationId,
            SubmissionId = submissionId,
            PersonId = personId,
            AttendeeType = attendeeType,
            Status = RegistrationStatus.Active,
            CharacterName = characterName,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc,
            FeedbackInvitedAtUtc = invitedAtUtc,
        });
        if (submittedAtUtc is not null || answers is not null || pinToChronicle || organizerNote is not null)
        {
            var json = answers is null
                ? "{}"
                : System.Text.Json.JsonSerializer.Serialize(answers);
            db.FeedbackResponses.Add(new FeedbackResponse
            {
                RegistrationId = registrationId,
                AnswersJson = json,
                LastEditedBy = FeedbackEditedBy.Self,
                CreatedAtUtc = invitedAtUtc ?? new DateTimeOffset(FixedUtc, TimeSpan.Zero),
                UpdatedAtUtc = submittedAtUtc ?? new DateTimeOffset(FixedUtc, TimeSpan.Zero),
                SubmittedAtUtc = submittedAtUtc,
                PinToChronicle = pinToChronicle,
                OrganizerNote = organizerNote,
            });
        }
        await db.SaveChangesAsync();
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => new(CreateDbContext());
    }
}
