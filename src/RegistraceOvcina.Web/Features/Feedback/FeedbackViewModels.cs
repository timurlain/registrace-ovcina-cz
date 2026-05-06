using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Feedback;

/// <summary>
/// View model for one attendee's feedback form. Includes the question schema
/// for the appropriate role, the attendee's existing answers, and the resolved
/// open/close window with a read-only flag derived from "now".
/// </summary>
public sealed record FeedbackView(
    int RegistrationId,
    int SubmissionId,
    int GameId,
    string GameName,
    int PersonId,
    string AttendeeFullName,
    AttendeeType AttendeeType,
    FeedbackQuestionSet Questions,
    IReadOnlyDictionary<string, string> Answers,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset EffectiveOpensAt,
    DateTimeOffset EffectiveClosesAt,
    bool IsReadOnly,
    string? ReadOnlyReason);

/// <summary>
/// Derived three-state status of a single Registration's feedback on the
/// organizer dashboard.
/// </summary>
/// <remarks>
/// Order is load-bearing: organizers want NotInvited at the top (action
/// required), then Waiting (chase), then Done (informational).
/// </remarks>
public enum FeedbackStatus
{
    NotInvited = 0,
    Waiting = 1,
    Done = 2,
}

/// <summary>
/// Result of <see cref="FeedbackService.SaveAsync"/>. <see cref="Persisted"/>
/// is <c>false</c> when the window is closed or not yet open; in that case
/// <see cref="RejectionReason"/> carries a stable code (<c>"Closed"</c> /
/// <c>"NotOpen"</c>) the UI can localize.
/// </summary>
public sealed record FeedbackSaveResult(bool Persisted, string? RejectionReason);

/// <summary>
/// Aggregate counts for the organizer feedback dashboard tile row, scoped to one game.
/// </summary>
/// <remarks>
/// <para><see cref="Total"/> = registration count (one row per attendee).</para>
/// <para><see cref="Invited"/> = registrations with <c>FeedbackInvitedAtUtc</c> set.</para>
/// <para><see cref="Done"/> = registrations whose <see cref="FeedbackResponse"/>
/// has <c>SubmittedAtUtc</c> set.</para>
/// <para><see cref="Waiting"/> = invited minus done. (Not-invited rows do not
/// count as waiting because organizers haven't asked them yet.)</para>
/// </remarks>
public sealed record FeedbackStats(
    int Total,
    int Invited,
    int Done,
    int Waiting);

/// <summary>
/// One-row-per-Registration projection for the organizer feedback dashboard.
/// </summary>
/// <remarks>
/// <see cref="LastEditedBy"/> is null when no <see cref="FeedbackResponse"/>
/// row exists yet (i.e. the attendee has neither saved a draft nor been
/// scribed for). <see cref="FeedbackToken"/> is null until the token service
/// has minted one — used by the per-row "copy link" button on the dashboard.
/// </remarks>
public sealed record FeedbackDashboardRow(
    int RegistrationId,
    int SubmissionId,
    string HouseholdName,
    string PersonFullName,
    AttendeeType AttendeeType,
    FeedbackStatus Status,
    FeedbackEditedBy? LastEditedBy,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    Guid? FeedbackToken);

/// <summary>
/// Filter for <see cref="FeedbackService.GetDashboardRowsAsync"/>. All
/// properties are optional; null means "no filter on this dimension".
/// </summary>
public sealed record FeedbackDashboardFilter(
    FeedbackStatus? Status = null,
    AttendeeType? AttendeeType = null,
    string? Search = null);

/// <summary>
/// One row per attendee on the household-facing submission detail card.
/// Drives the "Zpětná vazba k Ovčině {GameName}" tile that lets the registrant
/// scribe answers for each member of the submission.
/// </summary>
public sealed record SubmissionFeedbackAttendee(
    int RegistrationId,
    string PersonFullName,
    AttendeeType AttendeeType,
    FeedbackStatus Status);

/// <summary>
/// Lightweight game-meta projection for the organizer feedback dashboard
/// header: name + the effective open/close window (with the EndsAtUtc fallback
/// already resolved). Used to render the title, status pill, and date range.
/// </summary>
public sealed record FeedbackGameHeader(
    int GameId,
    string GameName,
    DateTimeOffset EffectiveOpensAt,
    DateTimeOffset EffectiveClosesAt);

/// <summary>
/// Editable per-game email template overrides. All four fields are optional —
/// a blank or null entry tells the renderer to fall back to its hardcoded
/// canonical default. Used by the organizer template editor to round-trip the
/// four <c>Game.Feedback*Template</c> columns.
/// </summary>
public sealed record FeedbackEmailTemplates(
    string? BundleSubjectTemplate,
    string? BundleHtmlTemplate,
    string? AdultIndividualSubjectTemplate,
    string? AdultIndividualHtmlTemplate);

/// <summary>
/// Lightweight projection of one game on the organizer "choose a game" page
/// (<c>/organizace/zpetna-vazba</c>): id, name, end timestamp, and a derived
/// pill state (<c>NotYetOpen</c> / <c>Open</c> / <c>Closed</c>) computed
/// against the effective window.
/// </summary>
public sealed record FeedbackGameChooserRow(
    int GameId,
    string GameName,
    DateTimeOffset EndsAtUtc,
    DateTimeOffset EffectiveOpensAt,
    DateTimeOffset EffectiveClosesAt);

/// <summary>
/// Read-only detail view of a single attendee's feedback response, used by the
/// organizer per-response page. Carries the question schema for the role plus
/// the existing answers so the page can render labelled cards even when an
/// answer is missing.
/// </summary>
/// <remarks>
/// <see cref="HasResponse"/> is <c>false</c> until the attendee (or a scribe)
/// saves at least one draft. The detail page uses it to decide whether to
/// expose the pin toggle / organizer note textarea — neither makes sense for
/// an empty response.
/// </remarks>
public sealed record FeedbackResponseDetail(
    int RegistrationId,
    int GameId,
    string GameName,
    int SubmissionId,
    string HouseholdName,
    string PersonFullName,
    AttendeeType AttendeeType,
    FeedbackStatus Status,
    FeedbackQuestionSet Questions,
    IReadOnlyDictionary<string, string> Answers,
    FeedbackEditedBy? LastEditedBy,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    bool HasResponse,
    bool PinToChronicle,
    string? OrganizerNote,
    Guid? FeedbackToken);
