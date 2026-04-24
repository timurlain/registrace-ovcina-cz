using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Stats;
using RegistraceOvcina.Web.Features.Submissions;

namespace RegistraceOvcina.Web.Tests.Features.Stats;

/// <summary>
/// Regression coverage for issue #181 — the dashboard's "bez plné platby"
/// counter and ExpectedTotal must reflect the same on-the-fly pricing recompute
/// that /organizace/platby uses, so a cancelled attendee doesn't make a fully
/// paid submission look unpaid.
/// </summary>
public sealed class GameStatsFinancialTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);
    private const int GameId = 1;

    [Fact]
    public async Task UnpaidCount_ignores_cancelled_attendees_when_remaining_active_is_paid()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);

        // Original household: 3 players → frozen ExpectedTotalAmount = 3600
        // After 1 cancellation: only 2 active, real cost = 2400, parent paid 2400.
        // Buggy code counts this as unpaid (2400 < 3600). Fixed code does not.
        await AddSubmissionAsync(options,
            submissionId: 1,
            persistedExpectedTotal: 3600m,
            activePlayers: 2,
            cancelledPlayers: 1,
            paidAmount: 2400m);

        var stats = await BuildStatsAsync(options);

        Assert.NotNull(stats);
        Assert.Equal(0, stats!.UnpaidSubmissionCount);
        Assert.Equal(2400m, stats.ExpectedTotal);
        Assert.Equal(2400m, stats.PaidTotal);
    }

    [Fact]
    public async Task UnpaidCount_still_counts_genuinely_underpaid_submission()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);

        // 2 active players, only half paid → must show as unpaid.
        await AddSubmissionAsync(options,
            submissionId: 1,
            persistedExpectedTotal: 2400m,
            activePlayers: 2,
            cancelledPlayers: 0,
            paidAmount: 1200m);

        var stats = await BuildStatsAsync(options);

        Assert.NotNull(stats);
        Assert.Equal(1, stats!.UnpaidSubmissionCount);
        Assert.Equal(2400m, stats.ExpectedTotal);
        Assert.Equal(1200m, stats.PaidTotal);
    }

    [Fact]
    public async Task ExpectedTotal_recomputes_when_all_attendees_cancelled()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);

        // Whole household cancelled but submission still Submitted.
        // Frozen ExpectedTotalAmount=1200 must NOT show in dashboard total.
        await AddSubmissionAsync(options,
            submissionId: 1,
            persistedExpectedTotal: 1200m,
            activePlayers: 0,
            cancelledPlayers: 1,
            paidAmount: 0m);

        var stats = await BuildStatsAsync(options);

        Assert.NotNull(stats);
        Assert.Equal(0m, stats!.ExpectedTotal);
        Assert.Equal(0, stats.UnpaidSubmissionCount);
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<GameStats?> BuildStatsAsync(DbContextOptions<ApplicationDbContext> options)
    {
        var pricing = new SubmissionPricingService(TimeProvider.System);
        var service = new GameStatsService(new TestDbContextFactory(options), pricing);
        return await service.GetGameStatsAsync(GameId);
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
            StartsAtUtc = FixedUtc.AddDays(7),
            EndsAtUtc = FixedUtc.AddDays(9),
            RegistrationClosesAtUtc = FixedUtc.AddDays(-2),
            MealOrderingClosesAtUtc = FixedUtc.AddDays(-5),
            PaymentDueAtUtc = FixedUtc.AddDays(5),
            PlayerBasePrice = 1200m,
            AdultHelperBasePrice = 800m,
            BankAccount = "x",
            BankAccountName = "y",
            VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
            IsPublished = true,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddSubmissionAsync(
        DbContextOptions<ApplicationDbContext> options,
        int submissionId,
        decimal persistedExpectedTotal,
        int activePlayers,
        int cancelledPlayers,
        decimal paidAmount)
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
            GameId = GameId,
            RegistrantUserId = userId,
            PrimaryContactName = "Domácnost " + submissionId,
            PrimaryEmail = $"u{submissionId}@example.cz",
            PrimaryPhone = "777000000",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = persistedExpectedTotal
        });
        await db.SaveChangesAsync();

        var personSeq = 0;
        for (var i = 0; i < activePlayers; i++)
        {
            personSeq++;
            AddPlayer(db, submissionId, personSeq, RegistrationStatus.Active);
        }
        for (var i = 0; i < cancelledPlayers; i++)
        {
            personSeq++;
            AddPlayer(db, submissionId, personSeq, RegistrationStatus.Cancelled);
        }

        if (paidAmount != 0m)
        {
            db.Payments.Add(new Payment
            {
                SubmissionId = submissionId,
                Amount = paidAmount,
                Currency = "CZK",
                RecordedAtUtc = FixedUtc,
                Method = PaymentMethod.BankTransfer
            });
        }

        await db.SaveChangesAsync();
    }

    private static void AddPlayer(ApplicationDbContext db, int submissionId, int personSeq, RegistrationStatus status)
    {
        var pid = submissionId * 100 + personSeq;
        // Distinct surnames so each player is the "first child" in its family group
        // and gets PlayerBasePrice unconditionally — keeps the test free of
        // tiered-pricing math.
        db.People.Add(new Person
        {
            Id = pid,
            FirstName = "Kid" + personSeq,
            LastName = "Family" + pid,
            BirthYear = 2015,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        });
        db.Registrations.Add(new Registration
        {
            Id = pid,
            SubmissionId = submissionId,
            PersonId = pid,
            AttendeeType = AttendeeType.Player,
            Status = status,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
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
