namespace RegistraceOvcina.Web.Features.Feedback;

/// <summary>
/// Configuration for the post-game Feedback feature. <see cref="PublicBaseUrl"/> is the
/// absolute origin to prefix every <c>/zpetna-vazba/{token}</c> link in outbound mail, so
/// we don't depend on whichever host actually served the request that triggered the send.
/// Mirrors <c>CharacterPrepOptions</c> shape for consistency: the URL is checked at
/// email-send time, not at startup, so a missing value doesn't crash the host (the
/// dashboard, form pages, and export still work without sending mail).
/// </summary>
public sealed class FeedbackOptions
{
    public const string SectionName = "Feedback";

    /// <summary>
    /// Absolute base URL, no trailing slash — e.g. <c>https://registrace.ovcina.cz</c>.
    /// Optional at startup: the mail service throws a clear <see cref="InvalidOperationException"/>
    /// if a send is attempted while this is null, mirroring CharacterPrep's behaviour.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// Address shown in email footers as "napiš nám". Optional — the mail service falls
    /// back to the shared mailbox address, and the renderer omits the contact sentence
    /// entirely when no address is available.
    /// </summary>
    public string? OrganizerContactEmail { get; set; }
}
