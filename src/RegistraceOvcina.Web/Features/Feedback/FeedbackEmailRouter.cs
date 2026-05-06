using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Feedback;

/// <summary>
/// Pure routing classifier for post-game feedback email dispatch.
/// </summary>
/// <remarks>
/// <para>Decides who gets which kind of email when an organizer hits "Poslat pozvánky"
/// or "Poslat připomínky". Static + side-effect-free so it is trivially testable
/// and shares no DI surface with the senders / renderer.</para>
///
/// <para>Routing rules per Registration:</para>
/// <list type="bullet">
///   <item><c>AttendeeType == Player</c> → bundle (kid).</item>
///   <item><c>AttendeeType != Player</c> AND <c>Person.Email</c> blank → bundle (adult, no email).</item>
///   <item><c>AttendeeType != Player</c> AND <c>Person.Email</c> set → individual.</item>
/// </list>
///
/// <para>The bundle always goes to the household's <see cref="RegistrationSubmission.PrimaryEmail"/>.
/// Individual adult emails always go to that adult's <see cref="Person.Email"/>.</para>
/// </remarks>
public static class FeedbackEmailRouter
{
    /// <summary>
    /// Classifies <paramref name="registrations"/> into one optional consolidated bundle
    /// (sent to the household contact) and zero or more individual adult emails.
    /// </summary>
    /// <param name="submission">Owns the contact email used for the bundle.</param>
    /// <param name="registrations">Registrations under that submission to classify. Caller
    /// must ensure they all share <paramref name="submission"/>'s identity; the router does
    /// not re-validate that.</param>
    /// <param name="game">Used for game name + closes-at copy in the rendered emails.</param>
    /// <param name="tokenLinkFor">Callback returning the absolute token URL for one
    /// registration. Kept as a callback so the router stays free of URL / config concerns.</param>
    /// <param name="isReminder">Forwarded to the produced models so the renderer picks
    /// the gentler reminder copy.</param>
    /// <returns>
    /// A tuple of (optional bundle, individual emails). Bundle is <c>null</c> when no
    /// registration needs to be bundled (i.e. every registration is an adult-with-email).
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when at least one registration would need to be bundled but
    /// <paramref name="submission"/>'s <see cref="RegistrationSubmission.PrimaryEmail"/>
    /// is blank — without a contact address we cannot deliver the bundle. Caller must
    /// pre-filter such submissions out of the bulk run.
    /// </exception>
    public static (FeedbackContactBundleEmail? bundle,
                   IReadOnlyList<FeedbackAdultIndividualEmail> individuals) Classify(
        RegistrationSubmission submission,
        IReadOnlyList<Registration> registrations,
        Game game,
        Func<Registration, string> tokenLinkFor,
        bool isReminder)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(tokenLinkFor);

        // Stable closes-at value for both bundle + individual models. Game.FeedbackClosesAtUtc
        // is nullable; if absent we surface MaxValue so the renderer can still format something
        // (the calling pipeline upstream is expected to refuse to send when the window is unset,
        // but the router itself doesn't gate on that — it stays purely structural).
        var closesAt = game.FeedbackClosesAtUtc ?? DateTimeOffset.MaxValue;

        var bundleEntries = new List<FeedbackBundleEntry>();
        var individuals = new List<FeedbackAdultIndividualEmail>();

        foreach (var registration in registrations)
        {
            var isPlayer = registration.AttendeeType == AttendeeType.Player;
            var personEmail = registration.Person?.Email;
            var hasIndividualEmail = !isPlayer && !string.IsNullOrWhiteSpace(personEmail);

            var attendeeName = ResolveAttendeeName(registration);
            var tokenLink = tokenLinkFor(registration);

            if (hasIndividualEmail)
            {
                individuals.Add(new FeedbackAdultIndividualEmail(
                    ToEmail: personEmail!,
                    AttendeeName: attendeeName,
                    GameName: game.Name,
                    FeedbackClosesAtLocal: closesAt,
                    TokenLink: tokenLink,
                    IsReminder: isReminder));
            }
            else
            {
                bundleEntries.Add(new FeedbackBundleEntry(
                    AttendeeName: attendeeName,
                    AttendeeType: registration.AttendeeType,
                    TokenLink: tokenLink));
            }
        }

        FeedbackContactBundleEmail? bundle = null;
        if (bundleEntries.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(submission.PrimaryEmail))
            {
                throw new ArgumentException(
                    $"Submission {submission.Id} has registrations that require bundling " +
                    $"but PrimaryEmail is blank — caller must pre-filter.",
                    nameof(submission));
            }

            bundle = new FeedbackContactBundleEmail(
                ToEmail: submission.PrimaryEmail,
                ContactName: string.IsNullOrWhiteSpace(submission.PrimaryContactName)
                    ? "rodiče"
                    : submission.PrimaryContactName,
                GameName: game.Name,
                FeedbackClosesAtLocal: closesAt,
                Entries: bundleEntries,
                IsReminder: isReminder);
        }

        return (bundle, individuals);
    }

    private static string ResolveAttendeeName(Registration registration)
    {
        var person = registration.Person;
        if (person is null)
        {
            return "";
        }

        var first = person.FirstName ?? "";
        var last = person.LastName ?? "";
        var combined = $"{first} {last}".Trim();
        return combined.Length == 0 ? "" : combined;
    }
}

/// <summary>
/// Consolidated post-game feedback email sent to the household contact, listing one row
/// per kid (and per adult-without-email) with their token link.
/// </summary>
public sealed record FeedbackContactBundleEmail(
    string ToEmail,
    string ContactName,
    string GameName,
    DateTimeOffset FeedbackClosesAtLocal,
    IReadOnlyList<FeedbackBundleEntry> Entries,
    bool IsReminder);

/// <summary>
/// One row in a <see cref="FeedbackContactBundleEmail"/> — a single attendee inside the
/// household whose feedback link is being delivered to the contact.
/// </summary>
public sealed record FeedbackBundleEntry(
    string AttendeeName,
    AttendeeType AttendeeType,
    string TokenLink);

/// <summary>
/// Direct post-game feedback email sent to an adult attendee at their own address.
/// </summary>
public sealed record FeedbackAdultIndividualEmail(
    string ToEmail,
    string AttendeeName,
    string GameName,
    DateTimeOffset FeedbackClosesAtLocal,
    string TokenLink,
    bool IsReminder);
