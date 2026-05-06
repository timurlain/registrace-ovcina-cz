using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace RegistraceOvcina.Web.Features.Feedback;

/// <summary>
/// Token-substitution renderer for organizer-supplied feedback email templates.
/// </summary>
/// <remarks>
/// <para>Two flavours:</para>
/// <list type="bullet">
///   <item><see cref="RenderHtml"/> — HTML body. Tokens listed in
///     <paramref name="tokens"/> as <em>plain</em> text (e.g. <c>{ContactName}</c>,
///     <c>{GameName}</c>) are HTML-encoded as they go in. Tokens listed as
///     <em>raw HTML</em> chunks (e.g. <c>{Entries}</c>, <c>{ButtonHtml}</c>,
///     <c>{ReminderIntro}</c>) are inserted verbatim — these are never
///     user-supplied at substitution time, they are pre-rendered server-side
///     and trusted.</item>
///   <item><see cref="RenderSubject"/> — plain-text subject. All tokens are
///     inserted verbatim (no HTML encoding) because the result is plain text
///     handed to Graph <c>sendMail</c>'s subject field.</item>
/// </list>
/// <para>Unknown tokens (e.g. an organizer types <c>{NotARealToken}</c>) are
/// left as-is. The fall-back keeps the system safe-by-default: a typo in the
/// editor never collapses to silently empty output.</para>
/// </remarks>
public static class FeedbackTemplateRenderer
{
    // Mirrors the encoder used in FeedbackEmailRenderer: keep Czech diacritics
    // legible in the rendered HTML source while still neutralising HTML special
    // chars. Numeric &#x...; entities work but turn the message source into
    // unreadable soup for anyone debugging an email.
    private static readonly HtmlEncoder UnicodeHtmlEncoder =
        HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>
    /// Substitutes <paramref name="tokens"/> into <paramref name="template"/>.
    /// Plain-text tokens (those listed in <paramref name="rawHtmlTokenKeys"/>
    /// are NOT plain-text) are HTML-encoded; raw-HTML tokens are inserted
    /// verbatim. Returns <paramref name="template"/> unchanged when null/blank.
    /// </summary>
    public static string RenderHtml(
        string? template,
        IReadOnlyDictionary<string, string> tokens,
        IReadOnlySet<string> rawHtmlTokenKeys)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template ?? string.Empty;
        }

        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(rawHtmlTokenKeys);

        var result = template;
        foreach (var (rawKey, value) in tokens)
        {
            // Tokens are written by organizers as `{Foo}` in their template.
            // Match that exact spelling case-sensitively to keep the contract
            // predictable.
            var placeholder = $"{{{rawKey}}}";
            var replacement = rawHtmlTokenKeys.Contains(rawKey)
                ? value ?? string.Empty
                : UnicodeHtmlEncoder.Encode(value ?? string.Empty);
            result = result.Replace(placeholder, replacement);
        }

        return result;
    }

    /// <summary>
    /// Substitutes <paramref name="tokens"/> into a plain-text subject template.
    /// No HTML encoding is applied — subjects are plain text in mail clients.
    /// Returns <paramref name="template"/> unchanged when null/blank.
    /// </summary>
    public static string RenderSubject(
        string? template,
        IReadOnlyDictionary<string, string> tokens)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template ?? string.Empty;
        }

        ArgumentNullException.ThrowIfNull(tokens);

        var result = template;
        foreach (var (rawKey, value) in tokens)
        {
            result = result.Replace($"{{{rawKey}}}", value ?? string.Empty);
        }

        return result;
    }

    // ------------------------------------------------------------ token keys
    // Keep these as constants so the renderer + editor agree on the spellings.

    public const string TokenContactName = "ContactName";
    public const string TokenAttendeeName = "AttendeeName";
    public const string TokenGameName = "GameName";
    public const string TokenDeadline = "Deadline";
    public const string TokenReminderPrefix = "ReminderPrefix";
    public const string TokenReminderIntro = "ReminderIntro";
    public const string TokenEntries = "Entries";
    public const string TokenTokenLink = "TokenLink";
    public const string TokenButtonHtml = "ButtonHtml";

    /// <summary>
    /// The set of tokens whose substitution value is pre-rendered HTML. These
    /// must NOT be HTML-encoded a second time; doing so would emit literal
    /// <c>&amp;lt;ul&amp;gt;</c> instead of a real list. All of these come from
    /// system code, never from user input.
    /// </summary>
    public static readonly IReadOnlySet<string> RawHtmlTokens =
        new HashSet<string>(StringComparer.Ordinal)
        {
            TokenEntries,
            TokenButtonHtml,
            TokenReminderIntro,
        };
}
