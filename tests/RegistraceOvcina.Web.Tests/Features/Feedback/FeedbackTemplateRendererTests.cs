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
}
