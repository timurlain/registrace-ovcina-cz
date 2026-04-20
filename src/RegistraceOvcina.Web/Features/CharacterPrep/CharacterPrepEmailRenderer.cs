using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace RegistraceOvcina.Web.Features.CharacterPrep;

/// <summary>
/// Immutable outcome of rendering a Character Prep email: both an HTML body (for mail clients
/// that render it) and a plain-text fallback (for clients that don't).
/// </summary>
public sealed record RenderedEmail(string Subject, string HtmlBody, string PlainTextBody);

/// <summary>
/// Everything the renderer needs to produce a Pozvánka or Připomínka email. Kept as a flat
/// record so the mail service can build it from the domain without the renderer touching EF.
/// </summary>
public sealed record CharacterPrepEmailModel(
    string GameName,
    DateTime GameStartDateUtc,
    string PrepUrl,
    IReadOnlyList<string> PlayerFullNames,
    IReadOnlyList<StartingEquipmentOptionView> Options,
    string OrganizerContactEmail);

public interface ICharacterPrepEmailRenderer
{
    RenderedEmail RenderPozvanka(CharacterPrepEmailModel model);
    RenderedEmail RenderPripominka(CharacterPrepEmailModel model);
}

/// <summary>
/// String-interpolation email renderer. We intentionally avoid Razor/MJML here because the
/// existing project email path (see <c>InvitationService</c> and <c>InboxService</c>) uses
/// raw HTML over the Graph <c>sendMail</c> endpoint — keeping a single rendering style keeps
/// debugging consistent with what already works.
/// </summary>
public sealed class CharacterPrepEmailRenderer : ICharacterPrepEmailRenderer
{
    // Czech locale for date formatting in the deadline line.
    private static readonly CultureInfo CzCulture = new("cs-CZ");

    // HtmlEncoder.Default escapes every non-ASCII character as &#x...; numeric entities,
    // which is safe but turns Czech diacritics ("Dvořák") into opaque soup in the message
    // source. We only need to neutralize the HTML special chars; UTF-8 carries the rest.
    private static readonly HtmlEncoder UnicodeHtmlEncoder =
        HtmlEncoder.Create(UnicodeRanges.All);

    public RenderedEmail RenderPozvanka(CharacterPrepEmailModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var subject = $"Příprava postav pro {model.GameName} — vyberte startovní výbavu";
        // Deadline = 3 days before game start; formatted "d. M. yyyy" (Czech short date).
        var deadline = model.GameStartDateUtc.AddDays(-3).ToString("d. M. yyyy", CzCulture);

        var html = BuildPozvankaHtml(model, deadline);
        var plain = BuildPozvankaPlain(model, deadline);

        return new RenderedEmail(subject, html, plain);
    }

    public RenderedEmail RenderPripominka(CharacterPrepEmailModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var subject = $"Připomínka: příprava postav pro {model.GameName}";

        var encodedUrl = UnicodeHtmlEncoder.Encode(model.PrepUrl);
        var html = new StringBuilder()
            .Append("<p>Ahoj, neviděli jsme tě ještě na stránce s přípravou postav. ")
            .Append("Odkaz je tady: ")
            .Append("<a href=\"").Append(encodedUrl).Append("\">").Append(encodedUrl).Append("</a>. ")
            .Append("Děkujeme!<br/>— Organizátoři</p>")
            .ToString();

        var plain = new StringBuilder()
            .AppendLine("Ahoj, neviděli jsme tě ještě na stránce s přípravou postav.")
            .AppendLine($"Odkaz je tady: {model.PrepUrl}")
            .AppendLine("Děkujeme!")
            .Append("— Organizátoři")
            .ToString();

        return new RenderedEmail(subject, html, plain);
    }

    private static string BuildPozvankaHtml(CharacterPrepEmailModel model, string deadline)
    {
        var encodedUrl = UnicodeHtmlEncoder.Encode(model.PrepUrl);
        var encodedGame = UnicodeHtmlEncoder.Encode(model.GameName);
        // Contact is optional; when blank the "napiš nám" sentence is suppressed
        // below so we never emit <a href="mailto:"></a>.
        var hasContact = !string.IsNullOrWhiteSpace(model.OrganizerContactEmail);
        var encodedContact = hasContact
            ? UnicodeHtmlEncoder.Encode(model.OrganizerContactEmail)
            : "";

        var sb = new StringBuilder(2048);

        sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;line-height:1.5;color:#222;\">");

        sb.Append("<p>Vážení rodiče,</p>");
        sb.Append("<p>chystáme <b>").Append(encodedGame).Append("</b> a těšíme se na Vás. Poučeni z minulých let bychom rádi zrychlili začátek hry — první volbu postav můžeme připravit s Vámi předem.</p>");
        sb.Append("<p>Prosíme Vás, abyste na odkazu níže vyplnili u každé postavy její herní jméno a počáteční výbavu. S dětmi si o tom, prosíme, promluvte.</p>");
        sb.Append("<p>Výběr pak předem připravíme přímo dětem do glejtu, takže se na začátku hry nikdo nemusí zdržovat.</p>");
        sb.Append("<p>Pokud si někdo nic nevybere, dostane 5 měďáků a může si potřebné vybavení koupit na začátku hry — jen se tím o něco zdrží.</p>");
        sb.Append("<p>Herní jméno postavy rádi zapíšeme do kronik a postava pak bude pokračovat i v dalších hrách.</p>");

        // Household player list
        if (model.PlayerFullNames.Count > 0)
        {
            sb.Append("<p>Máme od Vás přihlášené tyto děti:</p>");
            sb.Append("<ul>");
            foreach (var name in model.PlayerFullNames)
            {
                sb.Append("<li>").Append(UnicodeHtmlEncoder.Encode(name)).Append("</li>");
            }
            sb.Append("</ul>");
        }

        // Equipment options
        sb.Append("<p>Vyberte prosím každému dítěti jednu ze startovních výbav:</p>");
        sb.Append("<ul>");
        foreach (var option in model.Options)
        {
            sb.Append("<li><b>").Append(UnicodeHtmlEncoder.Encode(option.DisplayName)).Append("</b>");
            if (!string.IsNullOrWhiteSpace(option.Description))
            {
                sb.Append(" — ").Append(UnicodeHtmlEncoder.Encode(option.Description));
            }
            sb.Append("</li>");
        }
        sb.Append("</ul>");

        // CTA button + fallback plain URL
        sb.Append("<p style=\"margin:24px 0;\">")
          .Append("<a href=\"").Append(encodedUrl).Append("\" ")
          .Append("style=\"background:#2b6cb0;color:#fff;padding:10px 18px;text-decoration:none;border-radius:4px;display:inline-block;\">")
          .Append("Otevřít přípravu postav</a></p>");
        sb.Append("<p style=\"font-size:12px;color:#666;\">")
          .Append("Pokud tlačítko nefunguje, zkopíruj si tento odkaz do prohlížeče:<br/>")
          .Append("<a href=\"").Append(encodedUrl).Append("\">").Append(encodedUrl).Append("</a>")
          .Append("</p>");

        // Deadline
        sb.Append("<p>Prosíme o vyplnění do <b>").Append(deadline).Append("</b>.</p>");

        // Organizer contact — only render when we actually have an address, otherwise
        // we'd emit <a href="mailto:"></a> which some clients render as a dead link.
        if (hasContact)
        {
            sb.Append("<p>Když něco nefunguje, napište nám na ")
              .Append("<a href=\"mailto:").Append(encodedContact).Append("\">").Append(encodedContact).Append("</a>.</p>");
        }

        sb.Append("<p>Děkujeme!<br/>— Organizátoři</p>");
        sb.Append("</div>");

        return sb.ToString();
    }

    private static string BuildPozvankaPlain(CharacterPrepEmailModel model, string deadline)
    {
        var sb = new StringBuilder(1024);

        sb.AppendLine("Vážení rodiče,");
        sb.AppendLine();
        sb.Append("chystáme ").Append(model.GameName).AppendLine(" a těšíme se na Vás. Poučeni z minulých let bychom rádi zrychlili začátek hry — první volbu postav můžeme připravit s Vámi předem.");
        sb.AppendLine();
        sb.AppendLine("Prosíme Vás, abyste na odkazu níže vyplnili u každé postavy její herní jméno a počáteční výbavu. S dětmi si o tom, prosíme, promluvte.");
        sb.AppendLine();
        sb.AppendLine("Výběr pak předem připravíme přímo dětem do glejtu, takže se na začátku hry nikdo nemusí zdržovat.");
        sb.AppendLine();
        sb.AppendLine("Pokud si někdo nic nevybere, dostane 5 měďáků a může si potřebné vybavení koupit na začátku hry — jen se tím o něco zdrží.");
        sb.AppendLine();
        sb.AppendLine("Herní jméno postavy rádi zapíšeme do kronik a postava pak bude pokračovat i v dalších hrách.");
        sb.AppendLine();

        if (model.PlayerFullNames.Count > 0)
        {
            sb.AppendLine("Máme od Vás přihlášené tyto děti:");
            foreach (var name in model.PlayerFullNames)
            {
                sb.Append(" - ").AppendLine(name);
            }
            sb.AppendLine();
        }

        sb.AppendLine("Vyberte prosím každému dítěti jednu ze startovních výbav:");
        foreach (var option in model.Options)
        {
            sb.Append(" - ").Append(option.DisplayName);
            if (!string.IsNullOrWhiteSpace(option.Description))
            {
                sb.Append(" — ").Append(option.Description);
            }
            sb.AppendLine();
        }
        sb.AppendLine();

        sb.Append("Odkaz na přípravu: ").AppendLine(model.PrepUrl);
        sb.AppendLine();
        sb.Append("Prosíme o vyplnění do ").Append(deadline).AppendLine(".");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(model.OrganizerContactEmail))
        {
            sb.Append("Když něco nefunguje, napište nám na ").Append(model.OrganizerContactEmail).AppendLine(".");
            sb.AppendLine();
        }
        sb.AppendLine("Děkujeme!");
        sb.Append("— Organizátoři");

        return sb.ToString();
    }
}
