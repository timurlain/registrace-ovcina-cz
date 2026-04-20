using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.CharacterPrep;

namespace RegistraceOvcina.Web.Tests.Features.CharacterPrep;

/// <summary>
/// Covers <see cref="CharacterPrepService.GetDashboardRowsAsync"/>: one row per Player
/// registration, status derivation, status filter, full-text search (person + character
/// name, case-insensitive), sort order (status asc → HouseholdName) and pagination.
/// </summary>
public sealed class CharacterPrepDashboardRowsTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset NowUtc = new(FixedUtc);
    private const int GameId = 1;
    private const int EquipmentId = 10;

    // --------------------------------------------------------------------- status derivation

    [Fact]
    public async Task Status_is_NotInvited_when_submission_has_no_invite_timestamp()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, household: "Alpha",
            invitedAtUtc: null,
            players: new[] { new PlayerSpec(hasName: true, hasEquipment: true) });

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var page = await service.GetDashboardRowsAsync(GameId, new DashboardFilter(null, null));

        var row = Assert.Single(page.Rows);
        Assert.Equal(CharacterPrepStatus.NotInvited, row.Status);
    }

    [Fact]
    public async Task Status_is_Waiting_when_invited_but_missing_character_name_or_equipment()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, household: "Alpha",
            invitedAtUtc: NowUtc,
            players: new[]
            {
                new PlayerSpec(hasName: true, hasEquipment: false),
                new PlayerSpec(hasName: false, hasEquipment: true),
            });

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var page = await service.GetDashboardRowsAsync(GameId, new DashboardFilter(null, null));

        Assert.All(page.Rows, r => Assert.Equal(CharacterPrepStatus.Waiting, r.Status));
        Assert.Equal(2, page.Rows.Count);
    }

    [Fact]
    public async Task Status_is_Done_when_invited_and_fully_populated()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, household: "Alpha",
            invitedAtUtc: NowUtc,
            players: new[] { new PlayerSpec(hasName: true, hasEquipment: true) });

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var page = await service.GetDashboardRowsAsync(GameId, new DashboardFilter(null, null));

        var row = Assert.Single(page.Rows);
        Assert.Equal(CharacterPrepStatus.Done, row.Status);
    }

    [Fact]
    public async Task Whitespace_only_character_name_counts_as_missing()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, household: "Alpha",
            invitedAtUtc: NowUtc,
            players: new[] { new PlayerSpec(hasName: true, hasEquipment: true) });

        // Force a blank character name directly to prove the status derivation trims.
        await using (var db = new ApplicationDbContext(options))
        {
            var reg = await db.Registrations.SingleAsync();
            reg.CharacterName = "   ";
            await db.SaveChangesAsync();
        }

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var page = await service.GetDashboardRowsAsync(GameId, new DashboardFilter(null, null));

        var row = Assert.Single(page.Rows);
        Assert.Equal(CharacterPrepStatus.Waiting, row.Status);
    }

    // --------------------------------------------------------------------- sort order

    [Fact]
    public async Task Rows_sorted_NotInvited_then_Waiting_then_Done_then_household()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        // Done, household "Z"
        await AddSubmissionAsync(options, submissionId: 1, household: "Zeta",
            invitedAtUtc: NowUtc,
            players: new[] { new PlayerSpec(hasName: true, hasEquipment: true) });
        // Waiting, household "B"
        await AddSubmissionAsync(options, submissionId: 2, household: "Beta",
            invitedAtUtc: NowUtc,
            players: new[] { new PlayerSpec(hasName: false, hasEquipment: false) });
        // NotInvited, household "M"
        await AddSubmissionAsync(options, submissionId: 3, household: "Mu",
            invitedAtUtc: null,
            players: new[] { new PlayerSpec(hasName: false, hasEquipment: false) });
        // NotInvited, household "A" (should come first)
        await AddSubmissionAsync(options, submissionId: 4, household: "Alpha",
            invitedAtUtc: null,
            players: new[] { new PlayerSpec(hasName: false, hasEquipment: false) });

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var page = await service.GetDashboardRowsAsync(GameId, new DashboardFilter(null, null));

        var statuses = page.Rows.Select(r => r.Status).ToArray();
        Assert.Equal(new[]
        {
            CharacterPrepStatus.NotInvited,
            CharacterPrepStatus.NotInvited,
            CharacterPrepStatus.Waiting,
            CharacterPrepStatus.Done
        }, statuses);

        // Within NotInvited, "Alpha" precedes "Mu".
        Assert.Equal("Alpha", page.Rows[0].HouseholdName);
        Assert.Equal("Mu", page.Rows[1].HouseholdName);
    }

    // --------------------------------------------------------------------- status filter

    [Fact]
    public async Task Status_filter_restricts_rows_to_requested_status()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, household: "A",
            invitedAtUtc: null,
            players: new[] { new PlayerSpec(hasName: false, hasEquipment: false) });
        await AddSubmissionAsync(options, submissionId: 2, household: "B",
            invitedAtUtc: NowUtc,
            players: new[] { new PlayerSpec(hasName: true, hasEquipment: true) });
        await AddSubmissionAsync(options, submissionId: 3, household: "C",
            invitedAtUtc: NowUtc,
            players: new[] { new PlayerSpec(hasName: false, hasEquipment: false) });

        var service = new CharacterPrepService(new TestDbContextFactory(options));

        var waiting = await service.GetDashboardRowsAsync(
            GameId, new DashboardFilter(CharacterPrepStatus.Waiting, null));
        Assert.Equal(1, waiting.Total);
        Assert.All(waiting.Rows, r => Assert.Equal(CharacterPrepStatus.Waiting, r.Status));

        var done = await service.GetDashboardRowsAsync(
            GameId, new DashboardFilter(CharacterPrepStatus.Done, null));
        Assert.Equal(1, done.Total);
        Assert.All(done.Rows, r => Assert.Equal(CharacterPrepStatus.Done, r.Status));

        var notInvited = await service.GetDashboardRowsAsync(
            GameId, new DashboardFilter(CharacterPrepStatus.NotInvited, null));
        Assert.Equal(1, notInvited.Total);
        Assert.All(notInvited.Rows, r => Assert.Equal(CharacterPrepStatus.NotInvited, r.Status));
    }

    // --------------------------------------------------------------------- search

    [Fact]
    public async Task Search_matches_person_name_case_insensitive()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionWithNamedPersonAsync(options, submissionId: 1, household: "H1",
            firstName: "Tomáš", lastName: "Pajonek", characterName: null);
        await AddSubmissionWithNamedPersonAsync(options, submissionId: 2, household: "H2",
            firstName: "Eva", lastName: "Dlouhá", characterName: null);

        var service = new CharacterPrepService(new TestDbContextFactory(options));

        var result = await service.GetDashboardRowsAsync(
            GameId, new DashboardFilter(null, "PAJ"));

        Assert.Equal(1, result.Total);
        Assert.Contains("Pajonek", result.Rows[0].PersonFullName);
    }

    [Fact]
    public async Task Search_matches_character_name_case_insensitive()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionWithNamedPersonAsync(options, submissionId: 1, household: "H1",
            firstName: "Kid", lastName: "One", characterName: "Legolas");
        await AddSubmissionWithNamedPersonAsync(options, submissionId: 2, household: "H2",
            firstName: "Kid", lastName: "Two", characterName: "Gimli");

        var service = new CharacterPrepService(new TestDbContextFactory(options));

        var result = await service.GetDashboardRowsAsync(
            GameId, new DashboardFilter(null, "legol"));

        Assert.Equal(1, result.Total);
        Assert.Equal("Legolas", result.Rows[0].CharacterName);
    }

    // --------------------------------------------------------------------- pagination

    [Fact]
    public async Task Paging_respects_page_and_page_size()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        for (var i = 0; i < 7; i++)
        {
            await AddSubmissionAsync(options, submissionId: i + 1,
                household: "H" + i.ToString("D2"),
                invitedAtUtc: null,
                players: new[] { new PlayerSpec(hasName: false, hasEquipment: false) });
        }

        var service = new CharacterPrepService(new TestDbContextFactory(options));

        var page1 = await service.GetDashboardRowsAsync(
            GameId, new DashboardFilter(null, null), page: 1, pageSize: 3);
        var page2 = await service.GetDashboardRowsAsync(
            GameId, new DashboardFilter(null, null), page: 2, pageSize: 3);
        var page3 = await service.GetDashboardRowsAsync(
            GameId, new DashboardFilter(null, null), page: 3, pageSize: 3);

        Assert.Equal(7, page1.Total);
        Assert.Equal(3, page1.Rows.Count);
        Assert.Equal(3, page2.Rows.Count);
        Assert.Single(page3.Rows);

        // No overlap across pages.
        var ids = page1.Rows.Concat(page2.Rows).Concat(page3.Rows)
            .Select(r => r.RegistrationId).ToArray();
        Assert.Equal(ids.Distinct().Count(), ids.Length);
    }

    // --------------------------------------------------------------------- helpers

    private sealed record PlayerSpec(bool hasName, bool hasEquipment);

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static async Task SeedGameAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);
        db.Games.Add(new Game
        {
            Id = GameId, Name = "Ovčina 2026",
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
            UpdatedAtUtc = FixedUtc
        });
        db.StartingEquipmentOptions.Add(new StartingEquipmentOption
        {
            Id = EquipmentId, GameId = GameId, Key = "sword", DisplayName = "Meč", SortOrder = 1
        });
        await db.SaveChangesAsync();
    }

    // --------------------------------------------------------------------- soft-delete

    [Fact]
    public async Task Soft_deleted_submission_is_excluded_from_rows()
    {
        // GetDashboardRowsAsync has an explicit .Where(x => !x.Submission.IsDeleted)
        // filter on top of the global query filter. This test locks in that behaviour
        // so a future refactor cannot regress it silently.
        var options = CreateOptions();
        await SeedGameAsync(options);

        await AddSubmissionAsync(options, submissionId: 1, household: "Alive",
            invitedAtUtc: NowUtc,
            players: new[] { new PlayerSpec(hasName: true, hasEquipment: true) });

        await AddSubmissionAsync(options, submissionId: 2, household: "Ghost",
            invitedAtUtc: NowUtc,
            players: new[] { new PlayerSpec(hasName: true, hasEquipment: true) },
            isDeleted: true);

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var page = await service.GetDashboardRowsAsync(GameId, new DashboardFilter(null, null));

        var row = Assert.Single(page.Rows);
        Assert.Equal("Alive", row.HouseholdName);
        Assert.Equal(1, page.Total);
    }

    // --------------------------------------------------------------------- helpers

    private static async Task AddSubmissionAsync(
        DbContextOptions<ApplicationDbContext> options,
        int submissionId,
        string household,
        DateTimeOffset? invitedAtUtc,
        IReadOnlyList<PlayerSpec> players,
        bool isDeleted = false)
    {
        await using var db = new ApplicationDbContext(options);
        AddUserAndSubmission(db, submissionId, household, invitedAtUtc, isDeleted);
        await db.SaveChangesAsync();

        for (var i = 0; i < players.Count; i++)
        {
            var pid = submissionId * 100 + i + 1;
            db.People.Add(new Person
            {
                Id = pid, FirstName = "Kid" + i, LastName = "S" + submissionId, BirthYear = 2015,
                CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
            });
            db.Registrations.Add(new Registration
            {
                Id = pid,
                SubmissionId = submissionId,
                PersonId = pid,
                AttendeeType = AttendeeType.Player,
                Status = RegistrationStatus.Active,
                CharacterName = players[i].hasName ? "Hero " + i : null,
                StartingEquipmentOptionId = players[i].hasEquipment ? EquipmentId : null,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task AddSubmissionWithNamedPersonAsync(
        DbContextOptions<ApplicationDbContext> options,
        int submissionId, string household,
        string firstName, string lastName, string? characterName)
    {
        await using var db = new ApplicationDbContext(options);
        AddUserAndSubmission(db, submissionId, household, invitedAtUtc: NowUtc);
        var pid = submissionId * 100 + 1;
        db.People.Add(new Person
        {
            Id = pid, FirstName = firstName, LastName = lastName, BirthYear = 2015,
            CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
        });
        db.Registrations.Add(new Registration
        {
            Id = pid,
            SubmissionId = submissionId,
            PersonId = pid,
            AttendeeType = AttendeeType.Player,
            Status = RegistrationStatus.Active,
            CharacterName = characterName,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        });
        await db.SaveChangesAsync();
    }

    private static void AddUserAndSubmission(
        ApplicationDbContext db,
        int submissionId, string household, DateTimeOffset? invitedAtUtc,
        bool isDeleted = false)
    {
        var userId = "user-" + submissionId;
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            DisplayName = "U" + submissionId,
            Email = $"u{submissionId}@example.cz",
            NormalizedEmail = $"U{submissionId}@EXAMPLE.CZ",
            UserName = $"u{submissionId}@example.cz",
            NormalizedUserName = $"U{submissionId}@EXAMPLE.CZ",
            EmailConfirmed = true,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = FixedUtc
        });
        db.RegistrationSubmissions.Add(new RegistrationSubmission
        {
            Id = submissionId,
            GameId = GameId,
            RegistrantUserId = userId,
            PrimaryContactName = household,
            PrimaryEmail = $"u{submissionId}@example.cz",
            PrimaryPhone = "777000000",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200,
            CharacterPrepInvitedAtUtc = invitedAtUtc,
            IsDeleted = isDeleted
        });
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ApplicationDbContext(options));
    }
}
