using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Feedback;

namespace RegistraceOvcina.Web.Tests.Features.Feedback;

/// <summary>
/// Covers <see cref="FeedbackOptionsService"/> — the read-only service that
/// loads the per-game feedback question JSON columns from <c>Game</c> and
/// delegates parsing to <see cref="FeedbackQuestionParser"/>.
///
/// Mirrors the test infrastructure used by <c>CharacterPrepOptionsServiceTests</c>:
/// in-memory <see cref="ApplicationDbContext"/> + a private
/// <c>TestDbContextFactory</c> + a per-test <c>SeedGameAsync</c> helper.
/// </summary>
public sealed class FeedbackOptionsServiceTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    private const string KidJson = """[{"key":"k1","label":"Co se ti líbilo?"}]""";
    private const string AdultJson = """[{"key":"a1","label":"Co bys zlepšil?"}]""";

    [Fact]
    public async Task GetForRoleAsync_returns_kid_set_when_attendee_is_player()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1, kidsJson: KidJson, adultsJson: AdultJson);

        var service = new FeedbackOptionsService(new TestDbContextFactory(options));

        var set = await service.GetForRoleAsync(1, AttendeeType.Player, CancellationToken.None);

        var q = Assert.Single(set.Questions);
        Assert.Equal("k1", q.Key);
        Assert.Equal("Co se ti líbilo?", q.Label);
    }

    [Fact]
    public async Task GetForRoleAsync_returns_adult_set_for_non_player()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1, kidsJson: KidJson, adultsJson: AdultJson);

        var service = new FeedbackOptionsService(new TestDbContextFactory(options));

        var set = await service.GetForRoleAsync(1, AttendeeType.Adult, CancellationToken.None);

        var q = Assert.Single(set.Questions);
        Assert.Equal("a1", q.Key);
        Assert.Equal("Co bys zlepšil?", q.Label);
    }

    [Fact]
    public async Task GetForRoleAsync_returns_empty_set_when_json_is_null()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1, kidsJson: null, adultsJson: null);

        var service = new FeedbackOptionsService(new TestDbContextFactory(options));

        var kid = await service.GetForRoleAsync(1, AttendeeType.Player, CancellationToken.None);
        var adult = await service.GetForRoleAsync(1, AttendeeType.Adult, CancellationToken.None);

        Assert.Same(FeedbackQuestionSet.Empty, kid);
        Assert.Same(FeedbackQuestionSet.Empty, adult);
    }

    [Fact]
    public async Task GetBothSetsAsync_returns_both_when_both_set()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1, kidsJson: KidJson, adultsJson: AdultJson);

        var service = new FeedbackOptionsService(new TestDbContextFactory(options));

        var (kid, adult) = await service.GetBothSetsAsync(1, CancellationToken.None);

        Assert.Equal("k1", Assert.Single(kid.Questions).Key);
        Assert.Equal("a1", Assert.Single(adult.Questions).Key);
    }

    [Fact]
    public async Task GetBothSetsAsync_returns_empty_pair_when_game_unknown()
    {
        var options = CreateOptions();
        // Note: no Game seeded.

        var service = new FeedbackOptionsService(new TestDbContextFactory(options));

        var (kid, adult) = await service.GetBothSetsAsync(999, CancellationToken.None);

        Assert.Same(FeedbackQuestionSet.Empty, kid);
        Assert.Same(FeedbackQuestionSet.Empty, adult);
    }

    // ---------- helpers ----------

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static async Task SeedGameAsync(
        DbContextOptions<ApplicationDbContext> options,
        int gameId,
        string? kidsJson,
        string? adultsJson)
    {
        await using var db = new ApplicationDbContext(options);
        db.Games.Add(new Game
        {
            Id = gameId,
            Name = $"Hra {gameId}",
            StartsAtUtc = FixedUtc.AddDays(30),
            EndsAtUtc = FixedUtc.AddDays(32),
            RegistrationClosesAtUtc = FixedUtc.AddDays(20),
            MealOrderingClosesAtUtc = FixedUtc.AddDays(25),
            PaymentDueAtUtc = FixedUtc.AddDays(20),
            PlayerBasePrice = 1200,
            AdultHelperBasePrice = 800,
            BankAccount = "x",
            BankAccountName = "y",
            VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
            IsPublished = true,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc,
            FeedbackKidQuestionsJson = kidsJson,
            FeedbackAdultQuestionsJson = adultsJson,
        });
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
