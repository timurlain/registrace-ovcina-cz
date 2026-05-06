using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Feedback;

namespace RegistraceOvcina.Web.Tests.Features.Feedback;

public sealed class FeedbackEmailRouterTests
{
    private static RegistrationSubmission CreateSubmission(
        int id = 100,
        string primaryEmail = "rodic@example.com",
        string primaryContactName = "Eva Nováková") =>
        new()
        {
            Id = id,
            PrimaryEmail = primaryEmail,
            PrimaryContactName = primaryContactName,
            GroupName = "Novákovi",
        };

    private static Game CreateGame(
        int id = 1,
        string name = "Ovčina 2026",
        DateTimeOffset? closesAt = null) =>
        new()
        {
            Id = id,
            Name = name,
            FeedbackClosesAtUtc = closesAt ?? new DateTimeOffset(2026, 6, 30, 22, 0, 0, TimeSpan.Zero),
        };

    private static Registration CreateRegistration(
        int id,
        AttendeeType attendeeType,
        string firstName,
        string lastName,
        string? personEmail) =>
        new()
        {
            Id = id,
            AttendeeType = attendeeType,
            Person = new Person
            {
                Id = id,
                FirstName = firstName,
                LastName = lastName,
                Email = personEmail,
            },
        };

    private static string LinkFor(Registration r) => $"https://registrace.ovcina.cz/zpetna-vazba/REG-{r.Id}";

    [Fact]
    public void Classify_returns_bundle_for_player_attendees()
    {
        var submission = CreateSubmission();
        var game = CreateGame();
        var kid = CreateRegistration(1, AttendeeType.Player, "Jan", "Novák", personEmail: null);

        var (bundle, individuals) = FeedbackEmailRouter.Classify(
            submission, [kid], game, LinkFor, isReminder: false);

        Assert.NotNull(bundle);
        Assert.Equal("rodic@example.com", bundle.ToEmail);
        Assert.Single(bundle.Entries);
        Assert.Equal("Jan Novák", bundle.Entries[0].AttendeeName);
        Assert.Equal(AttendeeType.Player, bundle.Entries[0].AttendeeType);
        Assert.Empty(individuals);
    }

    [Fact]
    public void Classify_returns_individual_for_adult_with_email()
    {
        var submission = CreateSubmission();
        var game = CreateGame();
        var adult = CreateRegistration(2, AttendeeType.Adult, "Petr", "Novák",
            personEmail: "petr@example.com");

        var (bundle, individuals) = FeedbackEmailRouter.Classify(
            submission, [adult], game, LinkFor, isReminder: false);

        Assert.Null(bundle);
        Assert.Single(individuals);
        Assert.Equal("petr@example.com", individuals[0].ToEmail);
        Assert.Equal("Petr Novák", individuals[0].AttendeeName);
        Assert.Equal("https://registrace.ovcina.cz/zpetna-vazba/REG-2", individuals[0].TokenLink);
    }

    [Fact]
    public void Classify_bundles_adult_without_email_with_kids()
    {
        var submission = CreateSubmission();
        var game = CreateGame();
        var kid = CreateRegistration(1, AttendeeType.Player, "Jan", "Novák", personEmail: null);
        var adultNoEmail = CreateRegistration(2, AttendeeType.Adult, "Eva", "Nováková",
            personEmail: null);
        var adultBlankEmail = CreateRegistration(3, AttendeeType.Adult, "Babička", "Nováková",
            personEmail: "   "); // whitespace must count as missing

        var (bundle, individuals) = FeedbackEmailRouter.Classify(
            submission, [kid, adultNoEmail, adultBlankEmail], game, LinkFor, isReminder: false);

        Assert.NotNull(bundle);
        Assert.Equal(3, bundle.Entries.Count);
        Assert.Contains(bundle.Entries, e => e.AttendeeName == "Jan Novák");
        Assert.Contains(bundle.Entries, e => e.AttendeeName == "Eva Nováková");
        Assert.Contains(bundle.Entries, e => e.AttendeeName == "Babička Nováková");
        Assert.Empty(individuals);
    }

    [Fact]
    public void Classify_returns_null_bundle_when_only_adults_with_emails()
    {
        var submission = CreateSubmission(primaryEmail: "");
        var game = CreateGame();
        var a1 = CreateRegistration(1, AttendeeType.Adult, "Petr", "N", "petr@example.com");
        var a2 = CreateRegistration(2, AttendeeType.Adult, "Eva", "N", "eva@example.com");

        var (bundle, individuals) = FeedbackEmailRouter.Classify(
            submission, [a1, a2], game, LinkFor, isReminder: false);

        Assert.Null(bundle);
        Assert.Equal(2, individuals.Count);
        Assert.Equal("petr@example.com", individuals[0].ToEmail);
        Assert.Equal("eva@example.com", individuals[1].ToEmail);
    }

    [Fact]
    public void Classify_throws_when_bundle_needed_but_contact_email_missing()
    {
        var submission = CreateSubmission(primaryEmail: "");
        var game = CreateGame();
        var kid = CreateRegistration(1, AttendeeType.Player, "Jan", "Novák", personEmail: null);

        var ex = Assert.Throws<ArgumentException>(() =>
            FeedbackEmailRouter.Classify(submission, [kid], game, LinkFor, isReminder: false));
        Assert.Contains("PrimaryEmail", ex.Message);
    }

    [Fact]
    public void Classify_passes_isReminder_flag_to_models()
    {
        var submission = CreateSubmission();
        var game = CreateGame();
        var kid = CreateRegistration(1, AttendeeType.Player, "Jan", "Novák", personEmail: null);
        var adult = CreateRegistration(2, AttendeeType.Adult, "Petr", "Novák",
            personEmail: "petr@example.com");

        var (bundle, individuals) = FeedbackEmailRouter.Classify(
            submission, [kid, adult], game, LinkFor, isReminder: true);

        Assert.NotNull(bundle);
        Assert.True(bundle.IsReminder);
        Assert.Single(individuals);
        Assert.True(individuals[0].IsReminder);
    }

    [Fact]
    public void Classify_resolves_token_link_via_callback()
    {
        var submission = CreateSubmission();
        var game = CreateGame();
        var kid = CreateRegistration(1, AttendeeType.Player, "Jan", "N", personEmail: null);
        var adult = CreateRegistration(2, AttendeeType.Adult, "Petr", "N", "petr@example.com");
        var calls = new List<int>();

        string LinkSpy(Registration r)
        {
            calls.Add(r.Id);
            return $"https://example.test/zv/{r.Id:D4}";
        }

        var (bundle, individuals) = FeedbackEmailRouter.Classify(
            submission, [kid, adult], game, LinkSpy, isReminder: false);

        Assert.Equal([1, 2], calls);
        Assert.NotNull(bundle);
        Assert.Equal("https://example.test/zv/0001", bundle.Entries[0].TokenLink);
        Assert.Equal("https://example.test/zv/0002", individuals[0].TokenLink);
    }
}
