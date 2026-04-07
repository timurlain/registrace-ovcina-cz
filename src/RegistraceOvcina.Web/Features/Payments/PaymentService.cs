using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Submissions;

namespace RegistraceOvcina.Web.Features.Payments;

public sealed class PaymentService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    SubmissionPricingService pricingService,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<PaymentOverviewItem>> GetPaymentOverviewAsync(
        int? gameId = null,
        BalanceStatus? balanceFilter = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.RegistrationSubmissions
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status == SubmissionStatus.Submitted);

        if (gameId.HasValue)
        {
            query = query.Where(x => x.GameId == gameId.Value);
        }

        var projected = query
            .OrderByDescending(x => x.SubmittedAtUtc)
            .Select(x => new
            {
                x.Id,
                GameName = x.Game.Name,
                x.PrimaryContactName,
                x.PrimaryEmail,
                x.ExpectedTotalAmount,
                PaidAmount = x.Payments.Sum(p => p.Amount),
                x.PaymentVariableSymbol,
                x.VoluntaryDonation
            });

        if (balanceFilter.HasValue)
        {
            projected = balanceFilter.Value switch
            {
                BalanceStatus.Unpaid => projected.Where(x => x.ExpectedTotalAmount > 0 && x.PaidAmount <= 0),
                BalanceStatus.Underpaid => projected.Where(x => x.PaidAmount > 0 && x.PaidAmount < x.ExpectedTotalAmount),
                BalanceStatus.Balanced => projected.Where(x => x.ExpectedTotalAmount <= 0 || x.PaidAmount == x.ExpectedTotalAmount),
                BalanceStatus.Overpaid => projected.Where(x => x.PaidAmount > x.ExpectedTotalAmount),
                _ => projected
            };
        }

        var submissions = await projected.ToListAsync(cancellationToken);

        return submissions
            .Select(x => new PaymentOverviewItem(
                x.Id,
                x.GameName,
                x.PrimaryContactName,
                x.PrimaryEmail,
                x.ExpectedTotalAmount,
                x.PaidAmount,
                pricingService.CalculateBalanceStatus(x.ExpectedTotalAmount, x.PaidAmount),
                x.PaymentVariableSymbol,
                x.VoluntaryDonation))
            .ToList();
    }

    public async Task RecordPaymentAsync(
        int submissionId,
        decimal amount,
        PaymentMethod method,
        string? reference,
        string? note,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var exists = await db.RegistrationSubmissions
            .AnyAsync(x => x.Id == submissionId && !x.IsDeleted, cancellationToken);

        if (!exists)
        {
            throw new InvalidOperationException("Přihláška nebyla nalezena.");
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var payment = new Payment
        {
            SubmissionId = submissionId,
            Amount = amount,
            Currency = "CZK",
            RecordedAtUtc = nowUtc,
            RecordedByUserId = actorUserId,
            Method = method,
            Reference = reference?.Trim(),
            Note = note?.Trim()
        };

        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(Payment),
            EntityId = payment.Id.ToString(),
            Action = "PaymentRecorded",
            ActorUserId = actorUserId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                submissionId,
                amount,
                method = method.ToString(),
                reference,
                note
            })
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentHistoryItem>> GetPaymentHistoryAsync(
        int submissionId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Payments
            .AsNoTracking()
            .Where(x => x.SubmissionId == submissionId)
            .OrderByDescending(x => x.RecordedAtUtc)
            .Select(x => new PaymentHistoryItem(
                x.Id,
                x.Amount,
                x.Currency,
                x.Method,
                x.RecordedAtUtc,
                x.Reference,
                x.Note))
            .ToListAsync(cancellationToken);
    }
}

// --- View Models ---

public sealed record PaymentOverviewItem(
    int SubmissionId,
    string GameName,
    string ContactName,
    string ContactEmail,
    decimal ExpectedTotal,
    decimal PaidAmount,
    BalanceStatus BalanceStatus,
    string? VariableSymbol,
    decimal VoluntaryDonation = 0m);

public sealed record PaymentHistoryItem(
    int Id,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    DateTime RecordedAtUtc,
    string? Reference,
    string? Note);
