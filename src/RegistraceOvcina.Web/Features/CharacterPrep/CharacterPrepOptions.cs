namespace RegistraceOvcina.Web.Features.CharacterPrep;

/// <summary>
/// Configuration for the Character Prep feature. <see cref="PublicBaseUrl"/> is the
/// absolute origin to prefix every <c>/postavy/{token}</c> link in outbound mail, so we
/// don't depend on whichever host actually served the request that triggered the send.
/// </summary>
public sealed class CharacterPrepOptions
{
    public const string SectionName = "CharacterPrep";

    /// <summary>
    /// Absolute base URL, no trailing slash — e.g. <c>https://registrace.ovcina.cz</c>.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// Address shown in email footers as "napiš nám". Usually the shared organizer inbox.
    /// </summary>
    public string? OrganizerContactEmail { get; set; }
}
