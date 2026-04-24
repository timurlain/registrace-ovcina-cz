using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Payments;
using RegistraceOvcina.Web.Features.Submissions;

namespace RegistraceOvcina.Web.Tests.Features.Payments;

/// <summary>
/// Issue #169 — organizers must be able to record refunds (vratky) by entering
/// a negative amount. Zero is still rejected (it would be a no-op record).
/// </summary>
public sealed class PaymentServiceRefundTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 24, 12, 0, 0, DateTimeKind.Utc);
    private const int SubmissionId = 42;
    private const string ActorUserId = "actor-1";

    [Fact]
    public async Task RecordPaymentAsync_persists_negative_amount_for_refund()
    {
        var options = CreateOptions();
        await SeedSubmissionAsync(options);

        var service = BuildService(options);
        await service.RecordPaymentAsync(
            SubmissionId,
            amount: -250m,
            PaymentMethod.BankTransfer,
            reference: "refund-001",
            note: "Vratka přeplatku",
            actorUserId: ActorUserId);

        await using var db = new ApplicationDbContext(options);
        var stored = await db.Payments.SingleAsync();
        Assert.Equal(-250m, stored.Amount);
        Assert.Equal(PaymentMethod.BankTransfer, stored.Method);
        Assert.Equal("refund-001", stored.Reference);
    }

    [Fact]
    public async Task RecordPaymentAsync_still_persists_positive_amount()
    {
        var options = CreateOptions();
        await SeedSubmissionAsync(options);

        var service = BuildService(options);
        await service.RecordPaymentAsync(
            SubmissionId,
            amount: 1500m,
            PaymentMethod.Cash,
            reference: null,
            note: null,
            actorUserId: ActorUserId);

        await using var db = new ApplicationDbContext(options);
        var stored = await db.Payments.SingleAsync();
        Assert.Equal(1500m, stored.Amount);
    }

    [Fact]
    public async Task RecordPaymentAsync_rejects_zero_amount()
    {
        var options = CreateOptions();
        await SeedSubmissionAsync(options);

        var service = BuildService(options);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordPaymentAsync(
                SubmissionId,
                amount: 0m,
                PaymentMethod.ManualAdjustment,
                reference: null,
                note: null,
                actorUserId: ActorUserId));

        Assert.Contains("nesmí být nulová", ex.Message);

        await using var db = new ApplicationDbContext(options);
        Assert.Empty(db.Payments);
    }

    // ---------------------------------------------------------------- helpers

    private static PaymentService BuildService(DbContextOptions<ApplicationDbContext> options) =>
        new(
            new TestDbContextFactory(options),
            new SubmissionPricingService(TimeProvider.System),
            TimeProvider.System);

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static async Task SeedSubmissionAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);
        db.Games.Add(new Game
        {
            Id = 1,
            Name = "Ovčina 2026",
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
        db.Users.Add(new ApplicationUser
        {
            Id = ActorUserId,
            DisplayName = "Org",
            Email = "org@example.cz",
            NormalizedEmail = "ORG@EXAMPLE.CZ",
            UserName = "org@example.cz",
            NormalizedUserName = "ORG@EXAMPLE.CZ",
            EmailConfirmed = true,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = FixedUtc
        });
        db.RegistrationSubmissions.Add(new RegistrationSubmission
        {
            Id = SubmissionId,
            GameId = 1,
            RegistrantUserId = ActorUserId,
            PrimaryContactName = "Domácnost X",
            PrimaryEmail = "x@example.cz",
            PrimaryPhone = "777000000",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1500m
        });
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
