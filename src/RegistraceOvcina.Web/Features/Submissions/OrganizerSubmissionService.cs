using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Submissions;

public sealed class OrganizerSubmissionService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    SubmissionPricingService pricingService,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<OrganizerSubmissionSummary>> GetSubmissionListAsync(
        int? gameId = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.RegistrationSubmissions
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (gameId.HasValue)
        {
            query = query.Where(x => x.GameId == gameId.Value);
        }

        var submissions = await query
            .OrderByDescending(x => x.SubmittedAtUtc ?? x.LastEditedAtUtc)
            .Select(x => new
            {
                x.Id,
                GameName = x.Game.Name,
                x.PrimaryContactName,
                x.PrimaryEmail,
                x.Status,
                AttendeeCount = x.Registrations.Count(r => r.Status == RegistrationStatus.Active),
                x.ExpectedTotalAmount,
                PaidAmount = x.Payments.Sum(p => p.Amount),
                x.SubmittedAtUtc
            })
            .ToListAsync(cancellationToken);

        return submissions
            .Select(x => new OrganizerSubmissionSummary(
                x.Id,
                x.GameName,
                x.PrimaryContactName,
                x.PrimaryEmail,
                x.Status,
                x.AttendeeCount,
                x.ExpectedTotalAmount,
                x.PaidAmount,
                pricingService.CalculateBalanceStatus(x.ExpectedTotalAmount, x.PaidAmount),
                x.SubmittedAtUtc))
            .ToList();
    }

    public async Task<OrganizerSubmissionDetail?> GetSubmissionDetailAsync(
        int submissionId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var submission = await db.RegistrationSubmissions
            .AsNoTracking()
            .Include(x => x.Game)
            .Include(x => x.Registrations).ThenInclude(r => r.Person)
            .Include(x => x.Registrations).ThenInclude(r => r.PreferredKingdom)
            .Include(x => x.Registrations).ThenInclude(r => r.FoodOrders).ThenInclude(fo => fo.MealOption)
            .Include(x => x.Payments)
            .Include(x => x.OrganizerNotes)
            .FirstOrDefaultAsync(x => x.Id == submissionId && !x.IsDeleted, cancellationToken);

        if (submission is null)
        {
            return null;
        }

        var emails = await db.EmailMessages
            .AsNoTracking()
            .Where(x => x.LinkedSubmissionId == submissionId)
            .OrderByDescending(x => x.ReceivedAtUtc ?? x.SentAtUtc)
            .ToListAsync(cancellationToken);

        var submissionEntityId = submissionId.ToString();
        var paymentEntityIds = submission.Payments
            .Select(p => p.Id.ToString())
            .ToList();
        var registrationEntityIds = submission.Registrations
            .Select(r => r.Id.ToString())
            .ToList();

        var auditLogs = await db.AuditLogs
            .AsNoTracking()
            .Where(x =>
                (x.EntityType == nameof(RegistrationSubmission) && x.EntityId == submissionEntityId)
                || (x.EntityType == nameof(Payment) && paymentEntityIds.Contains(x.EntityId))
                || (x.EntityType == nameof(Registration) && registrationEntityIds.Contains(x.EntityId)))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var paidAmount = submission.Payments.Sum(p => p.Amount);
        var balanceStatus = pricingService.CalculateBalanceStatus(submission.ExpectedTotalAmount, paidAmount);

        var timeline = BuildTimeline(submission, emails, auditLogs);

        var breakdown = pricingService.CalculateBreakdown(
            submission.Game, submission.Registrations, submission.VoluntaryDonation);

        return new OrganizerSubmissionDetail
        {
            Id = submission.Id,
            GameName = submission.Game.Name,
            Status = submission.Status,
            SubmittedAtUtc = submission.SubmittedAtUtc,
            PrimaryContactName = submission.PrimaryContactName,
            PrimaryEmail = submission.PrimaryEmail,
            PrimaryPhone = submission.PrimaryPhone,
            RegistrantNote = submission.RegistrantNote,
            ExpectedTotalAmount = submission.ExpectedTotalAmount,
            PaidAmount = paidAmount,
            BalanceStatus = balanceStatus,
            PaymentVariableSymbol = submission.PaymentVariableSymbol,
            Attendees = submission.Registrations
                .Where(r => r.Status == RegistrationStatus.Active)
                .OrderBy(r => r.CreatedAtUtc)
                .Select(r => new OrganizerAttendeeDetail
                {
                    RegistrationId = r.Id,
                    FirstName = r.Person.FirstName,
                    LastName = r.Person.LastName,
                    BirthYear = r.Person.BirthYear,
                    AttendeeType = r.AttendeeType,
                    PlayerSubType = r.PlayerSubType,
                    AdultRoles = r.AdultRoles,
                    PreferredKingdom = r.PreferredKingdom?.DisplayName,
                    CharacterName = r.CharacterName,
                    LodgingPreference = r.LodgingPreference,
                    ContactEmail = r.ContactEmail,
                    ContactPhone = r.ContactPhone,
                    RegistrantNote = r.RegistrantNote,
                    FoodOrders = r.FoodOrders
                        .OrderBy(fo => fo.MealDayUtc)
                        .Select(fo => new OrganizerFoodOrderDetail(
                            fo.MealDayUtc,
                            fo.MealOption.Name,
                            fo.Price))
                        .ToList()
                })
                .ToList(),
            Payments = submission.Payments
                .OrderByDescending(p => p.RecordedAtUtc)
                .Select(p => new OrganizerPaymentDetail(
                    p.Id,
                    p.Amount,
                    p.Currency,
                    p.Method,
                    p.RecordedAtUtc,
                    p.Reference,
                    p.Note))
                .ToList(),
            Notes = submission.OrganizerNotes
                .OrderByDescending(n => n.CreatedAtUtc)
                .Select(n => new OrganizerNoteDetail(
                    n.Id,
                    n.Note,
                    n.AuthorUserId,
                    n.CreatedAtUtc))
                .ToList(),
            Emails = emails
                .Select(e => new OrganizerEmailDetail(
                    e.Id,
                    e.Direction,
                    e.From,
                    e.To,
                    e.Subject,
                    e.ReceivedAtUtc ?? e.SentAtUtc))
                .ToList(),
            Timeline = timeline,
            PriceBreakdown = breakdown.Lines,
            VoluntaryDonation = submission.VoluntaryDonation
        };
    }

    public async Task AddNoteAsync(
        int submissionId,
        string note,
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

        var organizerNote = new OrganizerNote
        {
            SubmissionId = submissionId,
            AuthorUserId = actorUserId,
            Note = note.Trim(),
            CreatedAtUtc = nowUtc
        };

        db.OrganizerNotes.Add(organizerNote);
        await db.SaveChangesAsync(cancellationToken);

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(OrganizerNote),
            EntityId = organizerNote.Id.ToString(),
            Action = "NoteAdded",
            ActorUserId = actorUserId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new { submissionId })
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GameFilterItem>> GetGameFilterListAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Games
            .AsNoTracking()
            .OrderByDescending(x => x.StartsAtUtc)
            .Select(x => new GameFilterItem(x.Id, x.Name))
            .ToListAsync(cancellationToken);
    }

    private static List<TimelineEvent> BuildTimeline(
        RegistrationSubmission submission,
        List<EmailMessage> emails,
        List<AuditLog> auditLogs)
    {
        var events = new List<TimelineEvent>();

        // Notes
        foreach (var note in submission.OrganizerNotes)
        {
            events.Add(new TimelineEvent(
                note.CreatedAtUtc,
                TimelineEventType.Note,
                note.Note,
                note.AuthorUserId));
        }

        // Payments
        foreach (var payment in submission.Payments)
        {
            var desc = $"Platba {payment.Amount:0.00} {payment.Currency} ({GetPaymentMethodLabel(payment.Method)})";
            if (!string.IsNullOrWhiteSpace(payment.Note))
            {
                desc += $" — {payment.Note}";
            }

            events.Add(new TimelineEvent(
                payment.RecordedAtUtc,
                TimelineEventType.Payment,
                desc,
                payment.RecordedByUserId));
        }

        // Emails
        foreach (var email in emails)
        {
            var dateTime = email.ReceivedAtUtc ?? email.SentAtUtc ?? DateTime.MinValue;
            var direction = email.Direction == EmailDirection.Inbound ? "Přijatý" : "Odeslaný";
            events.Add(new TimelineEvent(
                dateTime,
                TimelineEventType.Email,
                $"{direction} e-mail: {email.Subject}",
                email.From));
        }

        // Status changes from audit log
        foreach (var log in auditLogs.Where(a =>
            a.Action is "SubmissionSubmitted" or "SubmissionDraftCreated" or "SubmissionCancelled"
            or "AttendeeAdded" or "AttendeeUpdated" or "AttendeeRemoved"))
        {
            events.Add(new TimelineEvent(
                log.CreatedAtUtc,
                TimelineEventType.StatusChange,
                GetAuditLogDescription(log),
                log.ActorUserId));
        }

        return events.OrderByDescending(x => x.OccurredAtUtc).ToList();
    }

    private static string GetAuditLogDescription(AuditLog log) => log.Action switch
    {
        "SubmissionDraftCreated" => "Přihláška vytvořena (koncept)",
        "SubmissionSubmitted" => "Přihláška odeslána",
        "SubmissionCancelled" => "Přihláška zrušena",
        "AttendeeAdded" => $"Účastník přidán{GetAttendeeNameFromDetails(log.DetailsJson)}",
        "AttendeeUpdated" => $"Účastník upraven{GetAttendeeNameFromDetails(log.DetailsJson)}",
        "AttendeeRemoved" => "Účastník odebrán",
        _ => log.Action
    };

    private static string GetAttendeeNameFromDetails(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return "";
        }

        try
        {
            using var doc = JsonDocument.Parse(detailsJson);
            var root = doc.RootElement;
            var firstName = root.TryGetProperty("FirstName", out var fn) ? fn.GetString() : null;
            var lastName = root.TryGetProperty("LastName", out var ln) ? ln.GetString() : null;
            if (!string.IsNullOrWhiteSpace(firstName) || !string.IsNullOrWhiteSpace(lastName))
            {
                return $": {firstName} {lastName}".TrimEnd();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed audit detail JSON — return empty suffix rather than crash timeline
        }

        return "";
    }

    private static string GetPaymentMethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.BankTransfer => "bankovní převod",
        PaymentMethod.Cash => "hotovost",
        PaymentMethod.ManualAdjustment => "ruční úprava",
        _ => method.ToString()
    };
}

// --- View Models ---

public sealed record OrganizerSubmissionSummary(
    int Id,
    string GameName,
    string PrimaryContactName,
    string PrimaryEmail,
    SubmissionStatus Status,
    int AttendeeCount,
    decimal ExpectedTotalAmount,
    decimal PaidAmount,
    BalanceStatus BalanceStatus,
    DateTime? SubmittedAtUtc);

public sealed record GameFilterItem(int Id, string Name);

public sealed class OrganizerSubmissionDetail
{
    public int Id { get; init; }
    public string GameName { get; init; } = "";
    public SubmissionStatus Status { get; init; }
    public DateTime? SubmittedAtUtc { get; init; }
    public string PrimaryContactName { get; init; } = "";
    public string PrimaryEmail { get; init; } = "";
    public string PrimaryPhone { get; init; } = "";
    public string? RegistrantNote { get; init; }
    public decimal ExpectedTotalAmount { get; init; }
    public decimal PaidAmount { get; init; }
    public BalanceStatus BalanceStatus { get; init; }
    public string? PaymentVariableSymbol { get; init; }
    public IReadOnlyList<OrganizerAttendeeDetail> Attendees { get; init; } = [];
    public IReadOnlyList<OrganizerPaymentDetail> Payments { get; init; } = [];
    public IReadOnlyList<OrganizerNoteDetail> Notes { get; init; } = [];
    public IReadOnlyList<OrganizerEmailDetail> Emails { get; init; } = [];
    public IReadOnlyList<TimelineEvent> Timeline { get; init; } = [];
    public IReadOnlyList<PriceBreakdownLine> PriceBreakdown { get; init; } = [];
    public decimal VoluntaryDonation { get; init; }
}

public sealed class OrganizerAttendeeDetail
{
    public int RegistrationId { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public int BirthYear { get; init; }
    public AttendeeType AttendeeType { get; init; }
    public PlayerSubType? PlayerSubType { get; init; }
    public AdultRoleFlags AdultRoles { get; init; }
    public string? PreferredKingdom { get; init; }
    public string? CharacterName { get; init; }
    public LodgingPreference? LodgingPreference { get; init; }
    public string? ContactEmail { get; init; }
    public string? ContactPhone { get; init; }
    public string? RegistrantNote { get; init; }
    public IReadOnlyList<OrganizerFoodOrderDetail> FoodOrders { get; init; } = [];
}

public sealed record OrganizerFoodOrderDetail(
    DateTime MealDayUtc,
    string MealOptionName,
    decimal Price);

public sealed record OrganizerPaymentDetail(
    int Id,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    DateTime RecordedAtUtc,
    string? Reference,
    string? Note);

public sealed record OrganizerNoteDetail(
    int Id,
    string Note,
    string AuthorUserId,
    DateTime CreatedAtUtc);

public sealed record OrganizerEmailDetail(
    int Id,
    EmailDirection Direction,
    string From,
    string To,
    string Subject,
    DateTime? DateUtc);

public enum TimelineEventType
{
    Note,
    Payment,
    StatusChange,
    Email
}

public sealed record TimelineEvent(
    DateTime OccurredAtUtc,
    TimelineEventType Type,
    string Description,
    string? ActorName);
