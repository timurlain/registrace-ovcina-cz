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
            .Include(x => x.Game)
            .Include(x => x.Registrations).ThenInclude(r => r.Person)
            .Include(x => x.Registrations).ThenInclude(r => r.FoodOrders)
            .Include(x => x.Payments)
            .AsSplitQuery()
            .Where(x => !x.IsDeleted && x.Status == SubmissionStatus.Submitted);

        if (gameId.HasValue)
        {
            query = query.Where(x => x.GameId == gameId.Value);
        }

        var submissions = await query
            .OrderByDescending(x => x.SubmittedAtUtc)
            .ToListAsync(cancellationToken);

        var results = submissions
            .Select(x =>
            {
                var paidAmount = x.Payments.Sum(p => p.Amount);
                var breakdown = pricingService.CalculateBreakdown(x.Game, x.Registrations, x.VoluntaryDonation);
                var computedTotal = breakdown.Total;
                return new PaymentOverviewItem(
                    x.Id,
                    x.Game.Name,
                    x.PrimaryContactName,
                    x.PrimaryEmail,
                    computedTotal,
                    paidAmount,
                    pricingService.CalculateBalanceStatus(computedTotal, paidAmount),
                    x.PaymentVariableSymbol,
                    x.VoluntaryDonation,
                    breakdown.Lines);
            })
            .ToList();

        if (balanceFilter.HasValue)
        {
            results = balanceFilter.Value switch
            {
                BalanceStatus.Unpaid => results.Where(x => x.ExpectedTotal > 0 && x.PaidAmount <= 0).ToList(),
                BalanceStatus.Underpaid => results.Where(x => x.PaidAmount > 0 && x.PaidAmount < x.ExpectedTotal).ToList(),
                BalanceStatus.Balanced => results.Where(x => x.ExpectedTotal <= 0 || x.PaidAmount == x.ExpectedTotal).ToList(),
                BalanceStatus.Overpaid => results.Where(x => x.PaidAmount > x.ExpectedTotal).ToList(),
                _ => results
            };
        }

        return results;
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
        // Payment.Amount is stored as numeric(18,2) in Postgres. Reject anything
        // that would silently round to 0.00 (e.g. 0.001) or anything with sub-haléř
        // precision the DB will round away — those are footguns, not refunds.
        if (decimal.Round(amount, 2, MidpointRounding.AwayFromZero) == 0m)
        {
            throw new InvalidOperationException("Částka nesmí být nulová.");
        }
        if (amount != decimal.Round(amount, 2, MidpointRounding.AwayFromZero))
        {
            throw new InvalidOperationException("Částka může mít nejvýše dvě desetinná místa.");
        }

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
    decimal VoluntaryDonation = 0m,
    IReadOnlyList<PriceBreakdownLine>? PriceBreakdown = null);

public sealed record PaymentHistoryItem(
    int Id,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    DateTime RecordedAtUtc,
    string? Reference,
    string? Note);
