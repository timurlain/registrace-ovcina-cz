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
