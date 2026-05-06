using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Feedback;

namespace RegistraceOvcina.Web.Tests.Features.Feedback;

public sealed class FeedbackEmailRendererTests
{
    private static readonly DateTimeOffset ClosesAt = new(2026, 6, 30, 22, 0, 0, TimeSpan.Zero);

    private static FeedbackContactBundleEmail CreateBundle(
        bool isReminder = false,
        string contactName = "Eva Nováková",
        IReadOnlyList<FeedbackBundleEntry>? entries = null) =>
        new(
            ToEmail: "rodic@example.com",
            ContactName: contactName,
            GameName: "Ovčina 2026",
            FeedbackClosesAtLocal: ClosesAt,
            Entries: entries ?? new[]
            {
                new FeedbackBundleEntry("Jan Novák", AttendeeType.Player, "https://registrace.ovcina.cz/zpetna-vazba/abc"),
                new FeedbackBundleEntry("Eva Nováková", AttendeeType.Adult, "https://registrace.ovcina.cz/zpetna-vazba/def"),
            },
            IsReminder: isReminder);

    private static FeedbackAdultIndividualEmail CreateAdult(bool isReminder = false) =>
        new(
            ToEmail: "petr@example.com",
            AttendeeName: "Petr Dvořák",
            GameName: "Ovčina 2026",
            FeedbackClosesAtLocal: ClosesAt,
            TokenLink: "https://registrace.ovcina.cz/zpetna-vazba/xyz",
            IsReminder: isReminder);

    [Fact]
    public void Bundle_invitation_subject_contains_game_name()
    {
        var renderer = new FeedbackEmailRenderer();
        var rendered = renderer.RenderContactBundle(CreateBundle());

        Assert.Contains("Ovčina 2026", rendered.Subject);
        Assert.Contains("Zpětná vazba", rendered.Subject);
        Assert.DoesNotContain("Připomínka", rendered.Subject);
    }

    [Fact]
    public void Bundle_reminder_subject_uses_reminder_prefix()
    {
        var renderer = new FeedbackEmailRenderer();
        var rendered = renderer.RenderContactBundle(CreateBundle(isReminder: true));

        Assert.Contains("Připomínka", rendered.Subject);
        Assert.Contains("Ovčina 2026", rendered.Subject);
    }

    [Fact]
    public void Bundle_html_contains_each_entry_link()
    {
        var renderer = new FeedbackEmailRenderer();
        var rendered = renderer.RenderContactBundle(CreateBundle());

        Assert.Contains("https://registrace.ovcina.cz/zpetna-vazba/abc", rendered.HtmlBody);
        Assert.Contains("https://registrace.ovcina.cz/zpetna-vazba/def", rendered.HtmlBody);
        Assert.Contains("Jan Novák", rendered.HtmlBody);
        Assert.Contains("Eva Nováková", rendered.HtmlBody);
    }

    [Fact]
    public void Bundle_html_labels_player_as_hrdina_and_adult_as_dospely()
    {
        var renderer = new FeedbackEmailRenderer();
        var rendered = renderer.RenderContactBundle(CreateBundle());

        Assert.Contains("hrdina", rendered.HtmlBody);
        Assert.Contains("dospělý", rendered.HtmlBody);
    }

    [Fact]
    public void Bundle_html_contains_deadline_in_czech_format()
    {
        var renderer = new FeedbackEmailRenderer();
        var rendered = renderer.RenderContactBundle(CreateBundle());

        Assert.Contains("30. 6. 2026", rendered.HtmlBody);
    }

    [Fact]
    public void Adult_individual_html_contains_attendee_name_and_link()
    {
        var renderer = new FeedbackEmailRenderer();
        var rendered = renderer.RenderAdultIndividual(CreateAdult());

        Assert.Contains("Petr Dvořák", rendered.HtmlBody);
        Assert.Contains("https://registrace.ovcina.cz/zpetna-vazba/xyz", rendered.HtmlBody);
    }

    [Fact]
    public void Adult_individual_reminder_uses_gentler_copy()
    {
        var renderer = new FeedbackEmailRenderer();
        var invitation = renderer.RenderAdultIndividual(CreateAdult(isReminder: false));
        var reminder = renderer.RenderAdultIndividual(CreateAdult(isReminder: true));

        // Invitation thanks the attendee for being there; reminder mentions the
        // missing response. The exact phrasing may evolve, but the tone-distinguishing
        // anchors should remain different between the two paths.
        Assert.Contains("Děkujeme, že jsi byl", invitation.HtmlBody);
        Assert.Contains("Posíláme jen tichou připomínku", reminder.HtmlBody);
    }
}
