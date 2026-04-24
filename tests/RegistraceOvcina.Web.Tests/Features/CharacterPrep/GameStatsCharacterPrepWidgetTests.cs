using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Stats;
using RegistraceOvcina.Web.Features.Submissions;

namespace RegistraceOvcina.Web.Tests.Features.CharacterPrep;

/// <summary>
/// The character-prep widget on /organizace/hry/{gameId}/statistiky reads
/// CharacterPrepFilled / CharacterPrepTotal from GameStats so the page doesn't
/// need to inject a second service.
/// </summary>
public sealed class GameStatsCharacterPrepWidgetTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
    private const int GameId = 1;
    private const int EquipmentId = 10;

    [Fact]
    public async Task Widget_shows_player_count_fully_filled_over_total()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);

        // Submission 1: 2 players, 1 fully filled.
        await AddSubmissionAsync(options, submissionId: 1,
            players: new[]
            {
                new PlayerSpec(hasName: true, hasEquipment: true),
                new PlayerSpec(hasName: false, hasEquipment: false),
            });
        // Submission 2: 1 player, fully filled.
        await AddSubmissionAsync(options, submissionId: 2,
            players: new[] { new PlayerSpec(hasName: true, hasEquipment: true) });
        // Submission 3: 1 player, missing equipment.
        await AddSubmissionAsync(options, submissionId: 3,
            players: new[] { new PlayerSpec(hasName: true, hasEquipment: false) });

        var service = new GameStatsService(new TestDbContextFactory(options), new SubmissionPricingService(TimeProvider.System));
        var stats = await service.GetGameStatsAsync(GameId);

        Assert.NotNull(stats);
        Assert.Equal(4, stats!.CharacterPrepTotal);
        Assert.Equal(2, stats.CharacterPrepFilled);
    }

    [Fact]
    public async Task Widget_excludes_soft_deleted_submissions()
    {
        // GameStatsService reads Registrations via navigation through Submission.
        // EF's HasQueryFilter on Registration (!x.Person.IsDeleted && !x.Submission.IsDeleted)
        // should hide the soft-deleted submission's players from both
        // CharacterPrepTotal and CharacterPrepFilled. This test locks that in so a
        // future LINQ refactor cannot drift away from the dashboard stats/rows methods.
        var options = CreateOptions();
        await SeedGameAsync(options);

        // Active submission: 1 fully-filled player.
        await AddSubmissionAsync(options, submissionId: 1,
            players: new[] { new PlayerSpec(hasName: true, hasEquipment: true) });

        // Soft-deleted submission: 1 fully-filled player — must NOT appear in widget counts.
        await AddSubmissionAsync(options, submissionId: 2,
            players: new[] { new PlayerSpec(hasName: true, hasEquipment: true) },
            isDeleted: true);

        var service = new GameStatsService(new TestDbContextFactory(options), new SubmissionPricingService(TimeProvider.System));
        var stats = await service.GetGameStatsAsync(GameId);

        Assert.NotNull(stats);
        Assert.Equal(1, stats!.CharacterPrepTotal);
        Assert.Equal(1, stats.CharacterPrepFilled);
    }

    [Fact]
    public async Task Widget_shows_zero_over_zero_when_no_players()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);

        var service = new GameStatsService(new TestDbContextFactory(options), new SubmissionPricingService(TimeProvider.System));
        var stats = await service.GetGameStatsAsync(GameId);

        Assert.NotNull(stats);
        Assert.Equal(0, stats!.CharacterPrepTotal);
        Assert.Equal(0, stats.CharacterPrepFilled);
    }

    // ------------------------------------------------------------ helpers

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

    private static async Task AddSubmissionAsync(
        DbContextOptions<ApplicationDbContext> options,
        int submissionId,
        IReadOnlyList<PlayerSpec> players,
        bool isDeleted = false)
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
            EmailConfirmed = true, IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = FixedUtc
        });
        db.RegistrationSubmissions.Add(new RegistrationSubmission
        {
            Id = submissionId,
            GameId = GameId,
            RegistrantUserId = userId,
            PrimaryContactName = "HH " + submissionId,
            PrimaryEmail = $"u{submissionId}@example.cz",
            PrimaryPhone = "777000000",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200,
            IsDeleted = isDeleted
        });
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

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ApplicationDbContext(options));
    }
}
