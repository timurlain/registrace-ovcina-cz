using RegistraceOvcina.Web.Features.CharacterPrep;

namespace RegistraceOvcina.Web.Tests.Features.CharacterPrep;

public sealed class CharacterPrepEmailRendererTests
{
    private static readonly DateTime GameStart = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    private static CharacterPrepEmailModel CreateModel(
        string gameName = "Ovčina 2026",
        string prepUrl = "https://registrace.ovcina.cz/postavy/abc123",
        string[]? playerNames = null,
        StartingEquipmentOptionView[]? options = null,
        string organizerContact = "organizatori@ovcina.cz") =>
        new(
            gameName,
            GameStart,
            prepUrl,
            playerNames ?? new[] { "Jan Novák", "Eva Nováková" },
            options ?? new[]
            {
                new StartingEquipmentOptionView(1, "tesak", "Tesák", "Krátký meč", 1),
                new StartingEquipmentOptionView(2, "luk", "Luk", "Lehký lovecký luk", 2),
                new StartingEquipmentOptionView(3, "hul", "Hůl", "Dřevěná bojová hůl", 3),
                new StartingEquipmentOptionView(4, "prak", "Prak", "Kožený prak", 4),
                new StartingEquipmentOptionView(5, "nuz", "Nůž", "Obyčejný lovecký nůž", 5),
            },
            organizerContact);

    [Fact]
    public void Pozvanka_subject_contains_game_name()
    {
        var renderer = new CharacterPrepEmailRenderer();
        var model = CreateModel(gameName: "Ovčina 2026 — Letní");

        var rendered = renderer.RenderPozvanka(model);

        Assert.Contains("Ovčina 2026 — Letní", rendered.Subject);
        Assert.Contains("Příprava postav", rendered.Subject);
    }

    [Fact]
    public void Pozvanka_html_contains_prep_url()
    {
        var renderer = new CharacterPrepEmailRenderer();
        var model = CreateModel(prepUrl: "https://registrace.ovcina.cz/postavy/XYZ-token-42");

        var rendered = renderer.RenderPozvanka(model);

        Assert.Contains("https://registrace.ovcina.cz/postavy/XYZ-token-42", rendered.HtmlBody);
    }

    [Fact]
    public void Pozvanka_html_lists_each_player_name()
    {
        var renderer = new CharacterPrepEmailRenderer();
        var model = CreateModel(playerNames: new[] { "Petr Dvořák", "Anna Dvořáková", "Lukáš Dvořák" });

        var rendered = renderer.RenderPozvanka(model);

        Assert.Contains("Petr Dvořák", rendered.HtmlBody);
        Assert.Contains("Anna Dvořáková", rendered.HtmlBody);
        Assert.Contains("Lukáš Dvořák", rendered.HtmlBody);
    }

    [Fact]
    public void Pozvanka_html_lists_each_equipment_option()
    {
        var renderer = new CharacterPrepEmailRenderer();
        var options = new[]
        {
            new StartingEquipmentOptionView(1, "tesak", "Tesák", "Krátký meč", 1),
            new StartingEquipmentOptionView(2, "luk", "Luk", "Lehký lovecký luk", 2),
            new StartingEquipmentOptionView(3, "hul", "Hůl", "Dřevěná bojová hůl", 3),
        };
        var model = CreateModel(options: options);

        var rendered = renderer.RenderPozvanka(model);

        Assert.Contains("Tesák", rendered.HtmlBody);
        Assert.Contains("Krátký meč", rendered.HtmlBody);
        Assert.Contains("Luk", rendered.HtmlBody);
        Assert.Contains("Lehký lovecký luk", rendered.HtmlBody);
        Assert.Contains("Hůl", rendered.HtmlBody);
        Assert.Contains("Dřevěná bojová hůl", rendered.HtmlBody);
    }

    [Fact]
    public void Pozvanka_html_contains_deadline_3_days_before_game_start()
    {
        var renderer = new CharacterPrepEmailRenderer();
        // Game starts 2026-06-15 — deadline line should contain 12. 6. 2026
        var model = CreateModel();

        var rendered = renderer.RenderPozvanka(model);

        Assert.Contains("12. 6. 2026", rendered.HtmlBody);
    }

    [Fact]
    public void Pripominka_subject_correct()
    {
        var renderer = new CharacterPrepEmailRenderer();
        var model = CreateModel(gameName: "Ovčina 2026");

        var rendered = renderer.RenderPripominka(model);

        Assert.Contains("Připomínka", rendered.Subject);
        Assert.Contains("Ovčina 2026", rendered.Subject);
    }

    [Fact]
    public void Pripominka_html_contains_prep_url_link()
    {
        var renderer = new CharacterPrepEmailRenderer();
        var model = CreateModel(prepUrl: "https://registrace.ovcina.cz/postavy/abc-xyz");

        var rendered = renderer.RenderPripominka(model);

        // must be rendered as a clickable <a href="..."> link
        Assert.Contains("href=\"https://registrace.ovcina.cz/postavy/abc-xyz\"", rendered.HtmlBody);
    }

    [Fact]
    public void Pripominka_plaintext_contains_url_inline()
    {
        var renderer = new CharacterPrepEmailRenderer();
        var model = CreateModel(prepUrl: "https://registrace.ovcina.cz/postavy/reminder-token");

        var rendered = renderer.RenderPripominka(model);

        Assert.Contains("https://registrace.ovcina.cz/postavy/reminder-token", rendered.PlainTextBody);
        // plain text body must have no HTML tags
        Assert.DoesNotContain("<a ", rendered.PlainTextBody);
        Assert.DoesNotContain("<br", rendered.PlainTextBody);
    }

    [Fact]
    public void Both_produce_nonempty_plaintext()
    {
        var renderer = new CharacterPrepEmailRenderer();
        var model = CreateModel();

        var pozvanka = renderer.RenderPozvanka(model);
        var pripominka = renderer.RenderPripominka(model);

        Assert.False(string.IsNullOrWhiteSpace(pozvanka.PlainTextBody));
        Assert.False(string.IsNullOrWhiteSpace(pripominka.PlainTextBody));
        // plaintext bodies must not contain HTML tags
        Assert.DoesNotContain("<", pozvanka.PlainTextBody);
        Assert.DoesNotContain("<", pripominka.PlainTextBody);
    }
}
