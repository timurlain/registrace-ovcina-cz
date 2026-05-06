using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Feedback;

/// <summary>
/// Immutable outcome of rendering a Feedback email: both an HTML body (for mail clients
/// that render it) and a plain-text fallback (for clients that don't). Mirrors the shape
/// returned by <c>CharacterPrepEmailRenderer</c> so the upcoming <c>FeedbackMailService</c>
/// can hand it straight to <see cref="IFeedbackEmailSender.SendAsync"/>.
/// </summary>
public sealed record RenderedFeedbackEmail(string Subject, string HtmlBody, string PlainTextBody);

public interface IFeedbackEmailRenderer
{
    RenderedFeedbackEmail RenderContactBundle(FeedbackContactBundleEmail model);
    RenderedFeedbackEmail RenderAdultIndividual(FeedbackAdultIndividualEmail model);
}

/// <summary>
/// String-builder Feedback email renderer. We intentionally mirror
/// <c>CharacterPrepEmailRenderer</c> (raw HTML over Graph <c>sendMail</c>, no Razor / MJML)
/// so a single rendering style stays consistent across the project's outbound mail paths
/// and debugging works the same way for both prep and feedback emails.
/// </summary>
public sealed class FeedbackEmailRenderer : IFeedbackEmailRenderer
{
    private static readonly CultureInfo CzCulture = new("cs-CZ");

    // HtmlEncoder.Default escapes every non-ASCII character as &#x...; numeric entities,
    // which is safe but turns Czech diacritics ("Dvořák") into opaque soup in the message
    // source. We only need to neutralize the HTML special chars; UTF-8 carries the rest.
    private static readonly HtmlEncoder UnicodeHtmlEncoder =
        HtmlEncoder.Create(UnicodeRanges.All);

    public RenderedFeedbackEmail RenderContactBundle(FeedbackContactBundleEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var subject = model.IsReminder
            ? $"Připomínka: zpětná vazba k {model.GameName}"
            : $"Zpětná vazba k {model.GameName} — pár otázek na závěr";

        var deadline = FormatDeadline(model.FeedbackClosesAtLocal);

        var html = BuildBundleHtml(model, deadline);
        var plain = BuildBundlePlain(model, deadline);

        return new RenderedFeedbackEmail(subject, html, plain);
    }

    public RenderedFeedbackEmail RenderAdultIndividual(FeedbackAdultIndividualEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var subject = model.IsReminder
            ? $"Připomínka: zpětná vazba k {model.GameName}"
            : $"Zpětná vazba k {model.GameName} — pár otázek na závěr";

        var deadline = FormatDeadline(model.FeedbackClosesAtLocal);

        var html = BuildAdultIndividualHtml(model, deadline);
        var plain = BuildAdultIndividualPlain(model, deadline);

        return new RenderedFeedbackEmail(subject, html, plain);
    }

    // --------------------------------------------------------- bundle (household contact)

    private static string BuildBundleHtml(FeedbackContactBundleEmail model, string deadline)
    {
        var encodedContact = UnicodeHtmlEncoder.Encode(model.ContactName);
        var encodedGame = UnicodeHtmlEncoder.Encode(model.GameName);

        var sb = new StringBuilder(2048);

        sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;line-height:1.5;color:#222;\">");

        sb.Append("<p>Ahoj ").Append(encodedContact).Append(",</p>");

        if (model.IsReminder)
        {
            sb.Append("<p>Posíláme jen tichou připomínku — zpětnou vazbu k <b>")
              .Append(encodedGame)
              .Append("</b> jsme od vás ještě nedostali. Pokud jste nestihli, pořád to jde, otázek je málo a stačí krátké poctivé věty.</p>");
        }
        else
        {
            sb.Append("<p>Chtěli bychom zachytit vaše dojmy z letošní <b>")
              .Append(encodedGame)
              .Append("</b>, dokud je vše čerstvé. Posíláme odkaz pro každého z vašich hrdinů — děti můžou odpovědět samy, nebo jim odpovědi zapíšete vy.</p>");
        }

        sb.Append("<ul>");
        foreach (var entry in model.Entries)
        {
            var encodedName = UnicodeHtmlEncoder.Encode(entry.AttendeeName);
            var encodedLink = UnicodeHtmlEncoder.Encode(entry.TokenLink);
            var roleLabel = entry.AttendeeType == AttendeeType.Player ? "hrdina" : "dospělý";

            sb.Append("<li><a href=\"").Append(encodedLink).Append("\">")
              .Append(encodedName)
              .Append("</a> <span style=\"color:#666;\">(")
              .Append(roleLabel)
              .Append(")</span></li>");
        }
        sb.Append("</ul>");

        sb.Append("<p>Otázek je pár, krátké poctivé věty stačí. Odkazy jsou otevřené do <b>")
          .Append(deadline)
          .Append("</b>, pak se zavřou.</p>");

        sb.Append("<p>Děkujeme,<br/>— Organizátoři Ovčiny</p>");
        sb.Append("</div>");

        return sb.ToString();
    }

    private static string BuildBundlePlain(FeedbackContactBundleEmail model, string deadline)
    {
        var sb = new StringBuilder(1024);

        sb.Append("Ahoj ").Append(model.ContactName).AppendLine(",");
        sb.AppendLine();

        if (model.IsReminder)
        {
            sb.Append("Posíláme jen tichou připomínku — zpětnou vazbu k ")
              .Append(model.GameName)
              .AppendLine(" jsme od vás ještě nedostali.");
            sb.AppendLine("Pokud jste nestihli, pořád to jde, otázek je málo a stačí krátké poctivé věty.");
        }
        else
        {
            sb.Append("Chtěli bychom zachytit vaše dojmy z letošní ")
              .Append(model.GameName)
              .AppendLine(", dokud je vše čerstvé.");
            sb.AppendLine("Posíláme odkaz pro každého z vašich hrdinů — děti můžou odpovědět samy, nebo jim odpovědi zapíšete vy.");
        }
        sb.AppendLine();

        foreach (var entry in model.Entries)
        {
            var roleLabel = entry.AttendeeType == AttendeeType.Player ? "hrdina" : "dospělý";
            sb.Append(" - ").Append(entry.AttendeeName).Append(" (").Append(roleLabel).Append("): ").AppendLine(entry.TokenLink);
        }
        sb.AppendLine();

        sb.Append("Odkazy jsou otevřené do ").Append(deadline).AppendLine(", pak se zavřou.");
        sb.AppendLine();
        sb.AppendLine("Děkujeme,");
        sb.Append("— Organizátoři Ovčiny");

        return sb.ToString();
    }

    // ------------------------------------------------------------------ adult individual

    private static string BuildAdultIndividualHtml(FeedbackAdultIndividualEmail model, string deadline)
    {
        var encodedName = UnicodeHtmlEncoder.Encode(model.AttendeeName);
        var encodedGame = UnicodeHtmlEncoder.Encode(model.GameName);
        var encodedLink = UnicodeHtmlEncoder.Encode(model.TokenLink);

        var sb = new StringBuilder(1024);

        sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;line-height:1.5;color:#222;\">");

        sb.Append("<p>Ahoj ").Append(encodedName).Append(",</p>");

        if (model.IsReminder)
        {
            sb.Append("<p>Posíláme jen tichou připomínku — zpětnou vazbu k <b>")
              .Append(encodedGame)
              .Append("</b> od tebe ještě nemáme. Pokud jsi nestihl odpovědět, pořád to jde.</p>");
        }
        else
        {
            sb.Append("<p>Děkujeme, že jsi byl s námi na <b>")
              .Append(encodedGame)
              .Append("</b>. Než to všechno vyšumí, rádi bychom slyšeli, jak ti hra připadala.</p>");
        }

        sb.Append("<p style=\"margin:20px 0;\">")
          .Append("<a href=\"").Append(encodedLink).Append("\" ")
          .Append("style=\"background:#2b6cb0;color:#fff;padding:10px 18px;text-decoration:none;border-radius:4px;display:inline-block;\">")
          .Append("Otevřít zpětnou vazbu</a></p>");
        sb.Append("<p style=\"font-size:12px;color:#666;\">")
          .Append("Pokud tlačítko nefunguje, zkopíruj si tento odkaz do prohlížeče:<br/>")
          .Append("<a href=\"").Append(encodedLink).Append("\">").Append(encodedLink).Append("</a>")
          .Append("</p>");

        sb.Append("<p>Otázek je pár, krátké poctivé věty stačí. Odkaz je otevřený do <b>")
          .Append(deadline)
          .Append("</b>.</p>");

        sb.Append("<p>Děkujeme,<br/>— Organizátoři Ovčiny</p>");
        sb.Append("</div>");

        return sb.ToString();
    }

    private static string BuildAdultIndividualPlain(FeedbackAdultIndividualEmail model, string deadline)
    {
        var sb = new StringBuilder(512);

        sb.Append("Ahoj ").Append(model.AttendeeName).AppendLine(",");
        sb.AppendLine();

        if (model.IsReminder)
        {
            sb.Append("Posíláme jen tichou připomínku — zpětnou vazbu k ")
              .Append(model.GameName)
              .AppendLine(" od tebe ještě nemáme.");
            sb.AppendLine("Pokud jsi nestihl odpovědět, pořád to jde.");
        }
        else
        {
            sb.Append("Děkujeme, že jsi byl s námi na ").Append(model.GameName).AppendLine(".");
            sb.AppendLine("Než to všechno vyšumí, rádi bychom slyšeli, jak ti hra připadala.");
        }
        sb.AppendLine();

        sb.Append("Odkaz na zpětnou vazbu: ").AppendLine(model.TokenLink);
        sb.AppendLine();
        sb.Append("Otázek je pár, krátké poctivé věty stačí. Odkaz je otevřený do ").Append(deadline).AppendLine(".");
        sb.AppendLine();
        sb.AppendLine("Děkujeme,");
        sb.Append("— Organizátoři Ovčiny");

        return sb.ToString();
    }

    // -------------------------------------------------------------------------- helpers

    private static string FormatDeadline(DateTimeOffset closesAt)
    {
        // DateTimeOffset.MaxValue is the router's sentinel for "game has no closes-at
        // configured". Falling back to a generic phrase keeps the email shippable even
        // in that edge case (the upstream pipeline is expected to refuse to send it
        // anyway, but defensive copy beats a 9999-formatted date).
        if (closesAt == DateTimeOffset.MaxValue)
        {
            return "konce zpětné vazby";
        }

        return closesAt.ToString("d. M. yyyy", CzCulture);
    }
}
