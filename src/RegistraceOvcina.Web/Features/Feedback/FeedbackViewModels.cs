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
