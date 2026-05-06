using RegistraceOvcina.Web.Features.Feedback;

namespace RegistraceOvcina.Web.Tests.Features.Feedback;

/// <summary>
/// Covers <see cref="FeedbackTemplateRenderer"/> — substitutes named tokens
/// (<c>{ContactName}</c>, <c>{Entries}</c>, etc.) into organizer-supplied
/// template strings. Plain-string tokens are HTML-encoded; pre-rendered HTML
/// chunks are inserted verbatim.
/// </summary>
public sealed class FeedbackTemplateRendererTests
{
    [Fact]
    public void RenderHtml_substitutes_plain_tokens()
    {
        var template = "<p>Ahoj {ContactName}, čeká tě {GameName}.</p>";
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ContactName"] = "Eva",
            ["GameName"] = "Ovčina 2026",
        };

        var rendered = FeedbackTemplateRenderer.RenderHtml(
            template, tokens, FeedbackTemplateRenderer.RawHtmlTokens);

        Assert.Contains("Ahoj Eva", rendered);
        Assert.Contains("Ovčina 2026", rendered);
    }

    [Fact]
    public void RenderHtml_leaves_unknown_tokens_untouched()
    {
        var template = "<p>Hello {ContactName} and {Mystery}.</p>";
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ContactName"] = "Eva",
        };

        var rendered = FeedbackTemplateRenderer.RenderHtml(
            template, tokens, FeedbackTemplateRenderer.RawHtmlTokens);

        Assert.Contains("Hello Eva and {Mystery}.", rendered);
    }

    [Fact]
    public void RenderHtml_html_encodes_plain_string_tokens()
    {
        var template = "<p>{ContactName}</p>";
        // Adversarial input: a contact name with a script tag. Plain-string
        // tokens MUST be HTML-encoded to prevent injection — otherwise an
        // attacker could ship a malicious contact name and have the renderer
        // emit a real <script> tag into a delivered email.
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ContactName"] = "<script>alert(1)</script>",
        };

        var rendered = FeedbackTemplateRenderer.RenderHtml(
            template, tokens, FeedbackTemplateRenderer.RawHtmlTokens);

        Assert.DoesNotContain("<script>", rendered);
        Assert.Contains("&lt;script&gt;", rendered);
    }

    [Fact]
    public void RenderHtml_does_not_double_encode_raw_html_tokens()
    {
        var template = "<div>{Entries}</div>";
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Entries"] = "<ul><li>Jan</li></ul>",
        };

        var rendered = FeedbackTemplateRenderer.RenderHtml(
            template, tokens, FeedbackTemplateRenderer.RawHtmlTokens);

        Assert.Contains("<ul><li>Jan</li></ul>", rendered);
        Assert.DoesNotContain("&lt;ul&gt;", rendered);
    }

    [Fact]
    public void RenderHtml_preserves_czech_diacritics_in_plain_tokens()
    {
        var template = "<p>{ContactName}</p>";
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ContactName"] = "Tomáš Dvořák",
        };

        var rendered = FeedbackTemplateRenderer.RenderHtml(
            template, tokens, FeedbackTemplateRenderer.RawHtmlTokens);

        // Czech diacritics survive verbatim — we use the unicode-friendly
        // encoder so the message source stays human-readable.
        Assert.Contains("Tomáš Dvořák", rendered);
    }

    [Fact]
    public void RenderSubject_substitutes_reminder_prefix()
    {
        var template = "{ReminderPrefix}Zpětná vazba k {GameName}";
        var inviteTokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ReminderPrefix"] = string.Empty,
            ["GameName"] = "Ovčina 2026",
        };
        var reminderTokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ReminderPrefix"] = "(připomínka) ",
            ["GameName"] = "Ovčina 2026",
        };

        var invite = FeedbackTemplateRenderer.RenderSubject(template, inviteTokens);
        var reminder = FeedbackTemplateRenderer.RenderSubject(template, reminderTokens);

        Assert.Equal("Zpětná vazba k Ovčina 2026", invite);
        Assert.Equal("(připomínka) Zpětná vazba k Ovčina 2026", reminder);
    }

    [Fact]
    public void RenderHtml_returns_input_when_template_is_null_or_empty()
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);

        Assert.Equal(string.Empty,
            FeedbackTemplateRenderer.RenderHtml(null, tokens, FeedbackTemplateRenderer.RawHtmlTokens));
        Assert.Equal(string.Empty,
            FeedbackTemplateRenderer.RenderHtml(string.Empty, tokens, FeedbackTemplateRenderer.RawHtmlTokens));
    }

    [Fact]
    public void RenderHtml_does_not_html_encode_reminder_intro()
    {
        // ReminderIntro is a system-supplied HTML chunk, so it must come through
        // verbatim — not encoded into &lt;p&gt;...
        var template = "<div>{ReminderIntro}<p>Body</p></div>";
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ReminderIntro"] = "<p>Posíláme jen tichou připomínku.</p>",
        };

        var rendered = FeedbackTemplateRenderer.RenderHtml(
            template, tokens, FeedbackTemplateRenderer.RawHtmlTokens);

        Assert.Contains("<p>Posíláme jen tichou připomínku.</p>", rendered);
        Assert.DoesNotContain("&lt;p&gt;Posíláme", rendered);
    }

    // -------------------------------------------------------------------------
    // Quill brace-encoding survival tests.
    //
    // When an organizer pastes a template into the rich-text editor, Quill
    // round-trips the HTML through its Delta model. Some `{Token}` braces
    // survive verbatim, others surface as HTML numeric entities (decimal or
    // hex, mixed-case) or URL-encoded `%7B`/`%7D` (especially inside `href`
    // attributes — that's how `{TokenLink}` gets mangled). The renderer must
    // accept all forms; otherwise users receive emails with literal
    // `{AttendeeName}` text in them. See bug report 2026-05-06.
    // -------------------------------------------------------------------------

    [Fact]
    public void RenderHtml_substitutes_decimal_entity_encoded_braces()
    {
        var template = "<p>Ahoj &#123;ContactName&#125;.</p>";
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ContactName"] = "Eva",
        };

        var rendered = FeedbackTemplateRenderer.RenderHtml(
            template, tokens, FeedbackTemplateRenderer.RawHtmlTokens);

        Assert.Contains("Ahoj Eva.", rendered);
        Assert.DoesNotContain("&#123;", rendered);
        Assert.DoesNotContain("&#125;", rendered);
    }

    [Fact]
    public void RenderHtml_substitutes_hex_entity_encoded_braces()
    {
        var template = "<p>Ahoj &#x7B;ContactName&#x7D;.</p>";
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ContactName"] = "Eva",
        };

        var rendered = FeedbackTemplateRenderer.RenderHtml(
            template, tokens, FeedbackTemplateRenderer.RawHtmlTokens);

        Assert.Contains("Ahoj Eva.", rendered);
        Assert.DoesNotContain("&#x7B;", rendered);
        Assert.DoesNotContain("&#x7D;", rendered);
    }

    [Fact]
    public void RenderHtml_substitutes_lowercase_hex_entity_encoded_braces()
    {
        var template = "<p>Ahoj &#x7b;ContactName&#x7d;.</p>";
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ContactName"] = "Eva",
        };

        var rendered = FeedbackTemplateRenderer.RenderHtml(
            template, tokens, FeedbackTemplateRenderer.RawHtmlTokens);

        Assert.Contains("Ahoj Eva.", rendered);
        Assert.DoesNotContain("&#x7b;", rendered);
        Assert.DoesNotContain("&#x7d;", rendered);
    }

    [Fact]
    public void RenderHtml_substitutes_url_encoded_braces_in_href()
    {
        // Real-world Quill mangling: href values pass through the browser's
        // URL sanitizer which percent-encodes the curly braces.
        var template = "<a href=\"%7BTokenLink%7D\">Otevřít</a>";
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FeedbackTemplateRenderer.TokenTokenLink] = "https://example.test/feedback/abc",
        };
        var rawHtmlTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            FeedbackTemplateRenderer.TokenTokenLink,
        };

        var rendered = FeedbackTemplateRenderer.RenderHtml(template, tokens, rawHtmlTokens);

        Assert.Contains("<a href=\"https://example.test/feedback/abc\">Otevřít</a>", rendered);
        Assert.DoesNotContain("%7B", rendered);
        Assert.DoesNotContain("%7D", rendered);
    }

    [Fact]
    public void RenderHtml_substitutes_mixed_encoding_in_one_template()
    {
        // The reported prod bug: Deadline survived as literal braces, the
        // others were mangled differently. Reproduce the scenario.
        var template = """
            <p>Ahoj &#123;AttendeeName&#125;,</p>
            <p>posíláme zpětnou vazbu k {GameName}, deadline {Deadline}.</p>
            <p><a href="%7BTokenLink%7D">&#x7B;ButtonHtml&#x7D;</a></p>
            """;
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AttendeeName"] = "Eva",
            ["GameName"] = "Ovčina 30",
            ["Deadline"] = "1. 6. 2026",
            ["TokenLink"] = "https://example.test/abc",
            ["ButtonHtml"] = "<button>Vyplnit</button>",
        };
        var rawHtmlTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "TokenLink",
            "ButtonHtml",
        };

        var rendered = FeedbackTemplateRenderer.RenderHtml(template, tokens, rawHtmlTokens);

        Assert.Contains("Ahoj Eva,", rendered);
        Assert.Contains("Ovčina 30", rendered);
        Assert.Contains("1. 6. 2026", rendered);
        Assert.Contains("href=\"https://example.test/abc\"", rendered);
        Assert.Contains("<button>Vyplnit</button>", rendered);
        Assert.DoesNotContain("AttendeeName", rendered);
        Assert.DoesNotContain("TokenLink", rendered);
        Assert.DoesNotContain("ButtonHtml", rendered);
    }

    [Fact]
    public void RenderHtml_does_not_decode_legitimate_entities()
    {
        // The fix targets only the brace entities. Legitimate HTML entities
        // (ampersands, non-breaking spaces, em-dashes, accented letters) and
        // legitimate URL-encoded characters (path segments containing %20)
        // must pass through verbatim — otherwise the email body breaks in
        // unrelated ways.
        var template =
            "<p>R&amp;D&nbsp;&mdash; spr&aacute;va.</p>" +
            "<a href=\"https://example.test/path%20with%20space\">link</a>";
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);

        var rendered = FeedbackTemplateRenderer.RenderHtml(
            template, tokens, FeedbackTemplateRenderer.RawHtmlTokens);

        Assert.Contains("&amp;", rendered);
        Assert.Contains("&nbsp;", rendered);
        Assert.Contains("&mdash;", rendered);
        Assert.Contains("&aacute;", rendered);
        Assert.Contains("%20", rendered);
    }

    [Fact]
    public void RenderSubject_substitutes_decimal_entity_encoded_braces()
    {
        var template = "Pozvánka pro &#123;ContactName&#125;";
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ContactName"] = "Eva",
        };

        var rendered = FeedbackTemplateRenderer.RenderSubject(template, tokens);

        Assert.Equal("Pozvánka pro Eva", rendered);
    }

    [Fact]
    public void RenderSubject_substitutes_hex_entity_encoded_braces()
    {
        var template = "Pozvánka pro &#x7B;ContactName&#x7D;";
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ContactName"] = "Eva",
        };

        var rendered = FeedbackTemplateRenderer.RenderSubject(template, tokens);

        Assert.Equal("Pozvánka pro Eva", rendered);
    }

    [Fact]
    public void RenderSubject_substitutes_lowercase_hex_entity_encoded_braces()
    {
        var template = "Pozvánka pro &#x7b;ContactName&#x7d;";
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ContactName"] = "Eva",
        };

        var rendered = FeedbackTemplateRenderer.RenderSubject(template, tokens);

        Assert.Equal("Pozvánka pro Eva", rendered);
    }

    [Fact]
    public void RenderSubject_does_not_decode_legitimate_entities()
    {
        var template = "R&amp;D&nbsp;&mdash; sprava";
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);

        var rendered = FeedbackTemplateRenderer.RenderSubject(template, tokens);

        Assert.Equal("R&amp;D&nbsp;&mdash; sprava", rendered);
    }
}
