using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.CharacterPrep;

namespace RegistraceOvcina.Web.Tests.Features.CharacterPrep;

public sealed class CharacterPrepServiceDashboardStatsTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset NowUtc = new(FixedUtc);
    private const int GameId = 1;
    private const int OtherGameId = 2;
    private const int EquipmentId = 100;
    private const int OtherEquipmentId = 200;

    [Fact]
    public async Task Empty_game_returns_all_zero_stats()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var stats = await service.GetDashboardStatsAsync(GameId, CancellationToken.None);

        Assert.Equal(0, stats.TotalHouseholds);
        Assert.Equal(0, stats.Invited);
        Assert.Equal(0, stats.FullyFilled);
        Assert.Equal(0, stats.Pending);
    }

    [Fact]
    public async Task Mixed_states_produce_correct_counts()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);

        // S1: 2 Player attendees, both fully filled, invited → FullyFilled
        await AddSubmissionAsync(options, GameId, submissionId: 1, invitedAtUtc: NowUtc,
            players: new[]
            {
                new PlayerSpec(hasName: true, hasEquipment: true),
                new PlayerSpec(hasName: true, hasEquipment: true)
            });

        // S2: 2 Player attendees, only one has equipment, invited → Invited but not Filled
        await AddSubmissionAsync(options, GameId, submissionId: 2, invitedAtUtc: NowUtc,
            players: new[]
            {
                new PlayerSpec(hasName: true, hasEquipment: true),
                new PlayerSpec(hasName: true, hasEquipment: false)
            });

        // S3: 1 Player attendee, empty fields, not invited → TotalHouseholds only
        await AddSubmissionAsync(options, GameId, submissionId: 3, invitedAtUtc: null,
            players: new[] { new PlayerSpec(hasName: false, hasEquipment: false) });

        // S4: 1 Player + 1 Adult, all Player fields filled, invited → FullyFilled (Adult ignored)
        await AddSubmissionAsync(options, GameId, submissionId: 4, invitedAtUtc: NowUtc,
            players: new[] { new PlayerSpec(hasName: true, hasEquipment: true) },
            adults: 1);

        // S5: only Adults (no Players) → excluded from TotalHouseholds
        await AddSubmissionAsync(options, GameId, submissionId: 5, invitedAtUtc: NowUtc,
            players: Array.Empty<PlayerSpec>(),
            adults: 2);

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var stats = await service.GetDashboardStatsAsync(GameId, CancellationToken.None);

        Assert.Equal(4, stats.TotalHouseholds);
        Assert.Equal(3, stats.Invited);
        Assert.Equal(2, stats.FullyFilled);
        Assert.Equal(2, stats.Pending);
    }

    [Fact]
    public async Task Submissions_for_another_game_are_not_counted()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await SeedOtherGameAsync(options);

        // GameId household
        await AddSubmissionAsync(options, GameId, submissionId: 1, invitedAtUtc: NowUtc,
            players: new[] { new PlayerSpec(hasName: true, hasEquipment: true) });

        // OtherGameId household — should be ignored by the GameId stats query
        await AddSubmissionAsync(options, OtherGameId, submissionId: 2, invitedAtUtc: NowUtc,
            players: new[]
            {
                new PlayerSpec(hasName: true, hasEquipment: true),
                new PlayerSpec(hasName: false, hasEquipment: false)
            },
            equipmentId: OtherEquipmentId);

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var stats = await service.GetDashboardStatsAsync(GameId, CancellationToken.None);

        Assert.Equal(1, stats.TotalHouseholds);
        Assert.Equal(1, stats.Invited);
        Assert.Equal(1, stats.FullyFilled);
        Assert.Equal(0, stats.Pending);
    }

    [Fact]
    public async Task Soft_deleted_submission_is_excluded()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);

        // Active submission counting toward stats
        await AddSubmissionAsync(options, GameId, submissionId: 1, invitedAtUtc: NowUtc,
            players: new[] { new PlayerSpec(hasName: true, hasEquipment: true) });

        // Soft-deleted submission — must be excluded via the query filter
        await AddSubmissionAsync(options, GameId, submissionId: 2, invitedAtUtc: NowUtc,
            players: new[] { new PlayerSpec(hasName: true, hasEquipment: true) },
            isDeleted: true);

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var stats = await service.GetDashboardStatsAsync(GameId, CancellationToken.None);

        Assert.Equal(1, stats.TotalHouseholds);
        Assert.Equal(1, stats.Invited);
        Assert.Equal(1, stats.FullyFilled);
        Assert.Equal(0, stats.Pending);
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static async Task SeedGameAsync(DbContextOptions<ApplicationDbContext> options)
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
            UpdatedAtUtc = FixedUtc
        });
        db.StartingEquipmentOptions.Add(new StartingEquipmentOption
        {
            Id = EquipmentId, GameId = GameId, Key = "tesak", DisplayName = "Tesák", SortOrder = 1
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedOtherGameAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);
        db.Games.Add(new Game
        {
            Id = OtherGameId,
            Name = "Ovčina 2027",
            StartsAtUtc = FixedUtc.AddDays(400),
            EndsAtUtc = FixedUtc.AddDays(402),
            RegistrationClosesAtUtc = FixedUtc.AddDays(390),
            MealOrderingClosesAtUtc = FixedUtc.AddDays(385),
            PaymentDueAtUtc = FixedUtc.AddDays(395),
            PlayerBasePrice = 1300,
            AdultHelperBasePrice = 900,
            BankAccount = "x",
            BankAccountName = "y",
            VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
            IsPublished = true,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        });
        db.StartingEquipmentOptions.Add(new StartingEquipmentOption
        {
            Id = OtherEquipmentId, GameId = OtherGameId, Key = "luk", DisplayName = "Luk", SortOrder = 1
        });
        await db.SaveChangesAsync();
    }

    private sealed record PlayerSpec(bool hasName, bool hasEquipment);

    private static async Task AddSubmissionAsync(
        DbContextOptions<ApplicationDbContext> options,
        int gameId,
        int submissionId,
        DateTimeOffset? invitedAtUtc,
        IReadOnlyList<PlayerSpec> players,
        int adults = 0,
        bool isDeleted = false,
        int equipmentId = EquipmentId)
    {
        await using var db = new ApplicationDbContext(options);
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
            GameId = gameId,
            RegistrantUserId = userId,
            PrimaryContactName = "Contact " + submissionId,
            PrimaryEmail = $"u{submissionId}@example.cz",
            PrimaryPhone = "777000000",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200,
            CharacterPrepInvitedAtUtc = invitedAtUtc,
            IsDeleted = isDeleted
        });
        await db.SaveChangesAsync();

        var personBaseId = submissionId * 1000;
        var regBaseId = submissionId * 1000;
        for (var i = 0; i < players.Count; i++)
        {
            var pid = personBaseId + i + 1;
            db.People.Add(new Person
            {
                Id = pid, FirstName = "Player" + i, LastName = "S" + submissionId, BirthYear = 2015,
                CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
            });
            db.Registrations.Add(new Registration
            {
                Id = regBaseId + i + 1,
                SubmissionId = submissionId,
                PersonId = pid,
                AttendeeType = AttendeeType.Player,
                Status = RegistrationStatus.Active,
                CharacterName = players[i].hasName ? "Hero " + i : null,
                StartingEquipmentOptionId = players[i].hasEquipment ? equipmentId : null,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc
            });
        }
        for (var i = 0; i < adults; i++)
        {
            var pid = personBaseId + 500 + i + 1;
            db.People.Add(new Person
            {
                Id = pid, FirstName = "Adult" + i, LastName = "S" + submissionId, BirthYear = 1985,
                CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
            });
            db.Registrations.Add(new Registration
            {
                Id = regBaseId + 500 + i + 1,
                SubmissionId = submissionId,
                PersonId = pid,
                AttendeeType = AttendeeType.Adult,
                Status = RegistrationStatus.Active,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc
            });
        }
        await db.SaveChangesAsync();
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ApplicationDbContext(options));
    }
}
