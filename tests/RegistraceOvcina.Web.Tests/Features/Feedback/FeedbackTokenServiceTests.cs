using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Feedback;

namespace RegistraceOvcina.Web.Tests.Features.Feedback;

/// <summary>
/// Covers <see cref="FeedbackTokenService"/> — manages per-Registration GUID
/// tokens for the public <c>/zpetna-vazba/{token}</c> path. Token is lazily
/// issued on first <c>EnsureTokenAsync</c> and never rotates.
/// <para>Test infrastructure mirrors <c>CharacterPrepTokenServiceTests</c> and
/// <c>CharacterPrepServiceSaveTests</c>: in-memory <see cref="ApplicationDbContext"/>
/// + private <c>TestDbContextFactory</c> + per-test seeding helpers.</para>
/// </summary>
public sealed class FeedbackTokenServiceTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task EnsureTokenAsync_assigns_new_guid_when_none_exists()
    {
        var options = CreateOptions();
        var registrationId = await SeedPlayerRegistrationAsync(options);
        var sut = new FeedbackTokenService(new TestDbContextFactory(options));

        var token = await sut.EnsureTokenAsync(registrationId, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, token);

        await using var verify = new ApplicationDbContext(options);
        var persisted = await verify.Registrations.SingleAsync(x => x.Id == registrationId);
        Assert.Equal(token, persisted.FeedbackToken);
    }

    [Fact]
    public async Task EnsureTokenAsync_is_idempotent()
    {
        var options = CreateOptions();
        var registrationId = await SeedPlayerRegistrationAsync(options);
        var sut = new FeedbackTokenService(new TestDbContextFactory(options));

        var first = await sut.EnsureTokenAsync(registrationId, CancellationToken.None);
        var second = await sut.EnsureTokenAsync(registrationId, CancellationToken.None);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task EnsureTokenAsync_throws_when_registration_missing()
    {
        var options = CreateOptions();
        var sut = new FeedbackTokenService(new TestDbContextFactory(options));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.EnsureTokenAsync(999, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_returns_registration_id_for_known_token()
    {
        var options = CreateOptions();
        var registrationId = await SeedPlayerRegistrationAsync(options);
        var sut = new FeedbackTokenService(new TestDbContextFactory(options));
        var token = await sut.EnsureTokenAsync(registrationId, CancellationToken.None);

        var resolved = await sut.ResolveAsync(token, CancellationToken.None);

        Assert.Equal(registrationId, resolved);
    }

    [Fact]
    public async Task ResolveAsync_returns_null_for_unknown_token()
    {
        var options = CreateOptions();
        var sut = new FeedbackTokenService(new TestDbContextFactory(options));

        var resolved = await sut.ResolveAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveAsync_returns_null_for_soft_deleted_submission()
    {
        var options = CreateOptions();
        var registrationId = await SeedPlayerRegistrationAsync(options);

        var sut = new FeedbackTokenService(new TestDbContextFactory(options));
        var token = await sut.EnsureTokenAsync(registrationId, CancellationToken.None);

        // Soft-delete the submission after the token has been issued. The token
        // must no longer resolve — defends against leaking a registration via a
        // previously emailed link after an organizer drops the submission.
        await using (var db = new ApplicationDbContext(options))
        {
            var submission = await db.RegistrationSubmissions.SingleAsync(x => x.Id == 1);
            submission.IsDeleted = true;
            await db.SaveChangesAsync();
        }

        var resolved = await sut.ResolveAsync(token, CancellationToken.None);

        Assert.Null(resolved);
    }

    // ---------- helpers ----------

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static async Task<int> SeedPlayerRegistrationAsync(
        DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);

        db.Users.Add(new ApplicationUser
        {
            Id = "user-1",
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
        });
        db.Games.Add(new Game
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
            BankAccount = "x",
            BankAccountName = "y",
            VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
            IsPublished = true,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        });
        db.RegistrationSubmissions.Add(new RegistrationSubmission
        {
            Id = 1,
            GameId = 1,
            RegistrantUserId = "user-1",
            PrimaryContactName = "Parent",
            PrimaryEmail = "parent@example.cz",
            PrimaryPhone = "777111222",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200
        });
        db.People.Add(new Person
        {
            Id = 1,
            FirstName = "Kid",
            LastName = "One",
            BirthYear = 2015,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        });
        db.Registrations.Add(new Registration
        {
            Id = 1,
            SubmissionId = 1,
            PersonId = 1,
            AttendeeType = AttendeeType.Player,
            Status = RegistrationStatus.Active,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        });

        await db.SaveChangesAsync();
        return 1;
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            new(CreateDbContext());
    }
}
