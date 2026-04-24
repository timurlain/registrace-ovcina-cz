using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Email;

namespace RegistraceOvcina.Web.Tests.Features.Email;

/// <summary>
/// Issue #183 — bulk-compose UI on /organizace/posta needs accurate per-game
/// and "all" recipient counts so organizers can see who they're about to email
/// before they confirm. Counts must dedup households shared across games and
/// must skip cancelled / soft-deleted submissions.
/// </summary>
public sealed class InboxBulkRecipientsTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Counts_dedup_household_shared_between_two_games()
    {
        var options = CreateOptions();
        await SeedAsync(options, builder =>
        {
            builder.AddGame(1, "Ovčina 2026");
            builder.AddGame(2, "Ovčina 2027");
            // Same household submits to both games with the same primary email.
            builder.AddSubmission(gameId: 1, primaryEmail: "rodina@example.cz");
            builder.AddSubmission(gameId: 2, primaryEmail: "rodina@example.cz");
            // Distinct household for game 1 only.
            builder.AddSubmission(gameId: 1, primaryEmail: "druha@example.cz");
        });

        var service = BuildService(options);
        var counts = await service.GetBulkRecipientCountsAsync();

        Assert.Equal(2, counts.AllCount); // dedup across games
        Assert.Equal(2, counts.CountByGameId[1]);
        Assert.Equal(1, counts.CountByGameId[2]);
    }

    [Fact]
    public async Task Counts_exclude_cancelled_and_soft_deleted_submissions()
    {
        var options = CreateOptions();
        await SeedAsync(options, builder =>
        {
            builder.AddGame(1, "Ovčina 2026");
            builder.AddSubmission(gameId: 1, primaryEmail: "active@example.cz");
            builder.AddSubmission(gameId: 1, primaryEmail: "cancelled@example.cz", status: SubmissionStatus.Cancelled);
            builder.AddSubmission(gameId: 1, primaryEmail: "deleted@example.cz", isDeleted: true);
        });

        var service = BuildService(options);
        var counts = await service.GetBulkRecipientCountsAsync();

        Assert.Equal(1, counts.AllCount);
        Assert.Equal(1, counts.CountByGameId[1]);
    }

    [Fact]
    public async Task GetBulkRecipientsAsync_normalizes_emails_to_lowercase()
    {
        var options = CreateOptions();
        await SeedAsync(options, builder =>
        {
            builder.AddGame(1, "Ovčina 2026");
            builder.AddSubmission(gameId: 1, primaryEmail: "Mixed.Case@Example.CZ");
            builder.AddSubmission(gameId: 1, primaryEmail: "mixed.case@example.cz");
        });

        var service = BuildService(options);
        var recipients = await service.GetBulkRecipientsAsync(gameId: 1);

        Assert.Single(recipients);
        Assert.Equal("mixed.case@example.cz", recipients[0]);
    }

    // ---------------------------------------------------------------- helpers

    private static InboxService BuildService(DbContextOptions<ApplicationDbContext> options) =>
        // Bulk-recipients queries don't touch Graph or HttpClient, so the optional
        // graph dependencies stay null. Mailbox config doesn't matter here either.
        new(
            new TestDbContextFactory(options),
            Options.Create(new MailboxEmailOptions()));

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static async Task SeedAsync(DbContextOptions<ApplicationDbContext> options, Action<SeedBuilder> configure)
    {
        await using var db = new ApplicationDbContext(options);
        var builder = new SeedBuilder(db);
        configure(builder);
        await db.SaveChangesAsync();
    }

    private sealed class SeedBuilder(ApplicationDbContext db)
    {
        private int _submissionSeq = 1;
        private int _userSeq = 1;

        public void AddGame(int id, string name)
        {
            db.Games.Add(new Game
            {
                Id = id,
                Name = name,
                StartsAtUtc = FixedUtc.AddDays(7),
                EndsAtUtc = FixedUtc.AddDays(9),
                RegistrationClosesAtUtc = FixedUtc.AddDays(-2),
                MealOrderingClosesAtUtc = FixedUtc.AddDays(-5),
                PaymentDueAtUtc = FixedUtc.AddDays(5),
                PlayerBasePrice = 1500m,
                AdultHelperBasePrice = 800m,
                BankAccount = "x",
                BankAccountName = "y",
                VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
                IsPublished = true,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc
            });
        }

        public void AddSubmission(
            int gameId,
            string primaryEmail,
            SubmissionStatus status = SubmissionStatus.Submitted,
            bool isDeleted = false)
        {
            var userId = $"user-{_userSeq++}";
            db.Users.Add(new ApplicationUser
            {
                Id = userId,
                DisplayName = userId,
                Email = $"{userId}@x.cz",
                NormalizedEmail = $"{userId.ToUpperInvariant()}@X.CZ",
                UserName = $"{userId}@x.cz",
                NormalizedUserName = $"{userId.ToUpperInvariant()}@X.CZ",
                EmailConfirmed = true,
                IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = FixedUtc
            });
            db.RegistrationSubmissions.Add(new RegistrationSubmission
            {
                Id = _submissionSeq++,
                GameId = gameId,
                RegistrantUserId = userId,
                PrimaryContactName = "HH " + _submissionSeq,
                PrimaryEmail = primaryEmail,
                PrimaryPhone = "777000000",
                Status = status,
                SubmittedAtUtc = FixedUtc,
                LastEditedAtUtc = FixedUtc,
                ExpectedTotalAmount = 1500m,
                IsDeleted = isDeleted
            });
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ApplicationDbContext(options));
    }
}
