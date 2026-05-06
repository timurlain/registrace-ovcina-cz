using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
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

        var result = NormaliseEncodedBraces(template);
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

        var result = NormaliseEncodedBraces(template);
        foreach (var (rawKey, value) in tokens)
        {
            result = result.Replace($"{{{rawKey}}}", value ?? string.Empty);
        }

        return result;
    }

    // Quill normalises pasted HTML through a Delta -> innerHTML round-trip.
    // Some `{Token}` placeholders survive as literal braces, but others get
    // re-encoded:
    //   * curly braces in attribute values (e.g. <a href="{TokenLink}">) tend
    //     to be URL-encoded to %7B / %7D by the browser's link sanitizer
    //   * curly braces in text nodes can also surface as HTML numeric
    //     entities &#123; / &#125; (decimal) or &#x7B; / &#x7D; (hex,
    //     either case) depending on the source clipboard payload
    // The plain string.Replace below only matches literal `{Token}`, so any
    // mangled form silently leaks into the rendered email. Pre-pass and
    // restore literal braces ONLY — never decode the rest of the body, or
    // legitimate `&amp;`/`&nbsp;` entities would also collapse.
    private static readonly Regex EncodedBracePattern = new(
        @"&#0*123;|&#0*125;|&#[xX]0*7[bBdD];|%7[bBdD]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static string NormaliseEncodedBraces(string template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        // Quick reject: no `&` and no `%` means nothing to decode.
        if (template.IndexOf('&') < 0 && template.IndexOf('%') < 0)
        {
            return template;
        }

        return EncodedBracePattern.Replace(template, static match =>
        {
            var token = match.Value;
            // Decimal entities encode 123 = '{', 125 = '}'.
            // Hex / URL-encoded forms use 7B = '{', 7D = '}' (case-insensitive).
            // Discriminator is the final hex digit before any trailing `;`
            // (entities) or end-of-token (URL-encoded).
            var discriminator = token.EndsWith(';') ? token[^2] : token[^1];
            return discriminator is '3' or 'B' or 'b' ? "{" : "}";
        });
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
