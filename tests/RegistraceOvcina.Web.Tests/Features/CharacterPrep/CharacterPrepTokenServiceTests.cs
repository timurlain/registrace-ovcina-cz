using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.CharacterPrep;

namespace RegistraceOvcina.Web.Tests.Features.CharacterPrep;

public sealed class CharacterPrepTokenServiceTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Regex Base64UrlPattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    [Fact]
    public async Task EnsureTokenAsync_generates_token_on_first_call()
    {
        var options = CreateOptions();
        var submissionId = await SeedSubmissionAsync(options, existingToken: null);

        var service = new CharacterPrepTokenService(new TestDbContextFactory(options));

        var token = await service.EnsureTokenAsync(submissionId, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.InRange(token.Length, 40, 48);
        Assert.Matches(Base64UrlPattern, token);

        await using var verify = new ApplicationDbContext(options);
        var persisted = await verify.RegistrationSubmissions.SingleAsync(x => x.Id == submissionId);
        Assert.Equal(token, persisted.CharacterPrepToken);
    }

    [Fact]
    public async Task EnsureTokenAsync_is_idempotent()
    {
        var options = CreateOptions();
        var submissionId = await SeedSubmissionAsync(options, existingToken: null);

        var service = new CharacterPrepTokenService(new TestDbContextFactory(options));

        var first = await service.EnsureTokenAsync(submissionId, CancellationToken.None);
        var second = await service.EnsureTokenAsync(submissionId, CancellationToken.None);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task RotateTokenAsync_replaces_existing_token()
    {
        var options = CreateOptions();
        var submissionId = await SeedSubmissionAsync(options, existingToken: null);

        var service = new CharacterPrepTokenService(new TestDbContextFactory(options));

        var original = await service.EnsureTokenAsync(submissionId, CancellationToken.None);
        var rotated = await service.RotateTokenAsync(submissionId, CancellationToken.None);

        Assert.NotEqual(original, rotated);
        Assert.Matches(Base64UrlPattern, rotated);

        var lookupOld = await service.FindBySubmissionTokenAsync(original, CancellationToken.None);
        Assert.Null(lookupOld);

        var lookupNew = await service.FindBySubmissionTokenAsync(rotated, CancellationToken.None);
        Assert.NotNull(lookupNew);
        Assert.Equal(submissionId, lookupNew!.Id);
    }

    [Fact]
    public async Task FindBySubmissionTokenAsync_returns_null_for_unknown()
    {
        var options = CreateOptions();
        await SeedSubmissionAsync(options, existingToken: "some-real-token");

        var service = new CharacterPrepTokenService(new TestDbContextFactory(options));

        var result = await service.FindBySubmissionTokenAsync("does-not-exist", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindBySubmissionTokenAsync_returns_null_for_soft_deleted_submission()
    {
        var options = CreateOptions();
        const string token = "active-token-value";
        var submissionId = await SeedSubmissionAsync(options, existingToken: token);

        await using (var db = new ApplicationDbContext(options))
        {
            var submission = await db.RegistrationSubmissions.SingleAsync(x => x.Id == submissionId);
            submission.IsDeleted = true;
            await db.SaveChangesAsync();
        }

        var service = new CharacterPrepTokenService(new TestDbContextFactory(options));

        var result = await service.FindBySubmissionTokenAsync(token, CancellationToken.None);

        Assert.Null(result);
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static async Task<int> SeedSubmissionAsync(
        DbContextOptions<ApplicationDbContext> options,
        string? existingToken)
    {
        var user = new ApplicationUser
        {
            Id = "user-" + Guid.NewGuid().ToString("N"),
            DisplayName = "Parent",
            Email = "parent@example.cz",
            NormalizedEmail = "PARENT@EXAMPLE.CZ",
            UserName = "parent@example.cz",
            NormalizedUserName = "PARENT@EXAMPLE.CZ",
            EmailConfirmed = true,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = FixedUtc
        };

        var game = new Game
        {
            Id = 1,
            Name = "Ovčina 2026",
            StartsAtUtc = FixedUtc.AddDays(30),
            EndsAtUtc = FixedUtc.AddDays(32),
            RegistrationClosesAtUtc = FixedUtc.AddDays(20),
            MealOrderingClosesAtUtc = FixedUtc.AddDays(15),
            PaymentDueAtUtc = FixedUtc.AddDays(25),
            PlayerBasePrice = 1200,
            AdultHelperBasePrice = 800,
            BankAccount = "123/0100",
            BankAccountName = "Ovčina",
            VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
            IsPublished = true,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        };

        var submission = new RegistrationSubmission
        {
            Id = 1,
            GameId = game.Id,
            RegistrantUserId = user.Id,
            PrimaryContactName = "Parent",
            PrimaryEmail = "parent@example.cz",
            PrimaryPhone = "777111222",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200,
            CharacterPrepToken = existingToken
        };

        await using var db = new ApplicationDbContext(options);
        db.Users.Add(user);
        db.Games.Add(game);
        db.RegistrationSubmissions.Add(submission);
        await db.SaveChangesAsync();

        return submission.Id;
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ApplicationDbContext(options));
    }
}
