using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Feedback;
using Xunit.Sdk;

namespace RegistraceOvcina.E2E;

/// <summary>
/// End-to-end smoke tests for the post-game feedback feature.
///
/// Scenarios (mirrors the design in the Task 13 brief):
///   1. <see cref="Feedback_PublicTokenPath_FillAndSubmit_Persists"/> —
///      anonymous parent opens <c>/zpetna-vazba/{token}</c>, types an answer,
///      clicks "Hotovo, odeslat", reloads, and the answer + submitted state
///      are still there. DB verifies AnswersJson + LastEditedBy = Self.
///   2. <see cref="Feedback_ScribePath_LoggedInParent_FillAndSubmit_AuditHouseholdContact"/>
///      — registrant signs in (test sign-in helper), navigates from the
///      submission detail page to the per-attendee scribe form, fills + submits,
///      and DB verifies LastEditedBy = HouseholdContact + the parent's PersonId.
///
/// Scenario 3 (organizer "Poslat pozvánky" round-trip) is intentionally
/// covered by integration / unit tests rather than this E2E pass — see the
/// commented-out skip on <see cref="OrganizerInviteFlow_marks_invited_after_send"/>.
/// </summary>
public sealed class FeedbackSmokeTests : IClassFixture<AppFixture>
{
    private const string AdminEmail = "admin@ovcina.test";
    private const string RegistrantEmail = "registrant@ovcina.test";

    private readonly AppFixture _fixture;

    public FeedbackSmokeTests(AppFixture fixture)
    {
        _fixture = fixture;
    }

    // ----------------------------------------------------------- Scenario 1
    [Fact]
    public async Task Feedback_PublicTokenPath_FillAndSubmit_Persists()
    {
        var seeded = await SeedFeedbackAsync();
        var token = await EnsureTokenAsync(seeded.RegistrationId);

        // Anonymous browser context — no auth cookies, just like a parent
        // following a one-click email link.
        await using var context = await _fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(
            $"{_fixture.BaseUrl}/zpetna-vazba/{token}",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForInteractiveReadyAsync(page);

        // Page header carries the game name + attendee name. Scope to the
        // FeedbackFormCard's <section> so we don't pick up the layout's
        // navbar links that share the .text-secondary class.
        var pageBody = await page.Locator("body").InnerTextAsync();
        Assert.Contains(seeded.GameName, pageBody);
        Assert.Contains("Anežka FeedbackOvá", pageBody);

        // The seeded kid schema starts with strongest_moment.
        var memo = page.GetByTestId("feedback-input-strongest_moment");
        await memo.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        const string Answer = "Bojoval jsem se salamandrem a vyhrál.";
        await memo.FillAsync(Answer);
        await memo.DispatchEventAsync("input"); // mirror Blazor @oninput binding

        // Click "Hotovo, odeslat" — Save+Submit in one go.
        await page.GetByTestId("feedback-submit").ClickAsync();

        var statusBanner = page.GetByTestId("feedback-status");
        try
        {
            await statusBanner.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        }
        catch (TimeoutException)
        {
            var bodyText = await page.Locator("body").InnerTextAsync();
            throw new XunitException(
                "Feedback submit produced no status message. Page body:\n" + bodyText);
        }

        var status = (await statusBanner.TextContentAsync())?.Trim();
        Assert.Equal("Hotovo, odesláno. Děkujeme!", status);
        await AssertNoBlazorErrorsAsync(page);

        // ── Reload — the typed answer must persist + submitted banner shows.
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForInteractiveReadyAsync(page);

        Assert.Equal(
            Answer,
            await page.GetByTestId("feedback-input-strongest_moment").InputValueAsync());
        await page.GetByTestId("feedback-submitted-banner").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 5000 });

        // ── DB verifies persistence + audit fields.
        var (answersJson, lastEditedBy, submittedAtUtc) = await ReadResponseAsync(seeded.RegistrationId);
        Assert.NotNull(submittedAtUtc);
        Assert.Equal(FeedbackEditedBy.Self, lastEditedBy);
        Assert.Contains(Answer, answersJson);
        Assert.Contains("strongest_moment", answersJson);
    }

    // ----------------------------------------------------------- Scenario 2
    [Fact]
    public async Task Feedback_ScribePath_LoggedInParent_FillAndSubmit_AuditHouseholdContact()
    {
        var seeded = await SeedFeedbackAsync(submissionPrefix: "Scribe");
        var registrantPersonId = await EnsureRegistrantPersonIdAsync();

        var page = await _fixture.Browser.NewPageAsync();
        await LoginAsync(page, RegistrantEmail);

        // Submission detail page — feedback card should render with the seeded
        // kid attendee + a "Vyplnit" button.
        await page.GotoAsync(
            $"{_fixture.BaseUrl}/prihlasky/{seeded.SubmissionId}",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForInteractiveReadyAsync(page);

        var card = page.GetByTestId("feedback-submission-card");
        await card.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        var fillLink = page.GetByTestId($"feedback-attendee-link-{seeded.RegistrationId}");
        Assert.Equal("Vyplnit", (await fillLink.TextContentAsync())?.Trim());
        await fillLink.ClickAsync();

        // Now on the scribe form. Same FeedbackFormCard, different host.
        await page.WaitForURLAsync($"**/prihlasky/{seeded.SubmissionId}/zpetna-vazba/{seeded.RegistrationId}");
        await WaitForInteractiveReadyAsync(page);

        var memo = page.GetByTestId("feedback-input-strongest_moment");
        await memo.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        const string Answer = "Anežka byla nadšená z dungeonu.";
        await memo.FillAsync(Answer);
        await memo.DispatchEventAsync("input");

        await page.GetByTestId("feedback-submit").ClickAsync();

        // Submission success surfaces as the green "Hotovo, odesláno: <date>"
        // banner — the host re-fetches the view after save, and SubmittedAtUtc
        // becomes non-null on reload. The transient feedback-status alert may
        // race with that re-render; the persistent banner is the reliable
        // signal that the round-trip completed.
        var submittedBanner = page.GetByTestId("feedback-submitted-banner");
        try
        {
            await submittedBanner.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        }
        catch (TimeoutException)
        {
            var bodyText = await page.Locator("body").InnerTextAsync();
            var errorText = await page.GetByTestId("feedback-error").IsVisibleAsync()
                ? await page.GetByTestId("feedback-error").TextContentAsync()
                : "(no feedback-error banner)";
            throw new XunitException(
                $"Scribe submit did not yield the submitted banner.\n"
                + $"Error banner: {errorText}\n"
                + $"Page body:\n{bodyText}\n"
                + $"Host diagnostics:\n{_fixture.GetDiagnostics()}");
        }
        await AssertNoBlazorErrorsAsync(page);

        // ── DB verifies audit trail records the household contact, not Self.
        var (answersJson, lastEditedBy, submittedAtUtc) = await ReadResponseAsync(seeded.RegistrationId);
        Assert.NotNull(submittedAtUtc);
        Assert.Equal(FeedbackEditedBy.HouseholdContact, lastEditedBy);
        Assert.Contains(Answer, answersJson);

        // The registrant is wired to a Person; the audit FK should point at
        // that Person row.
        var lastEditedByPersonId = await ReadLastEditedByPersonIdAsync(seeded.RegistrationId);
        Assert.Equal(registrantPersonId, lastEditedByPersonId);

        await page.CloseAsync();
    }

    // ----------------------------------------------------------- Scenario 3
    // OrganizerInviteFlow_marks_invited_after_send is intentionally NOT included.
    //
    // The existing E2E host (AppFixture) launches the Web app as a separate
    // `dotnet run` process so we can't swap IFeedbackEmailSender for a recording
    // double via DI. In Testing env, the registered sender is
    // UnconfiguredFeedbackEmailSender which throws; SendBulkAsync catches each
    // per-submission failure and increments the error counter, but
    // FeedbackService.MarkInvitedAsync is gated on a successful send, so
    // FeedbackInvitedAtUtc never gets stamped. The end-to-end "Pozváno: 2"
    // assertion would therefore never go green here.
    //
    // Coverage for the dashboard send-button -> mark-invited contract lives in:
    //   • FeedbackMailServiceTests (unit tests, full bundle/individual matrix)
    //   • FeedbackDashboardTests (stat tile + filter coverage)
    //   • FeedbackServiceTests.MarkInvitedAsync_* (audit semantics)
    // Manual smoke covers the in-browser dashboard click; documented in the
    // PR description.

    // ============================================================ helpers

    private async Task<SeededFeedback> SeedFeedbackAsync(string submissionPrefix = "Token")
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var db = new ApplicationDbContext(options);
        var registrant = await db.Users.SingleAsync(x => x.Email == RegistrantEmail);
        var nowUtc = DateTime.UtcNow;
        var startsAtUtc = DateTime.UtcNow.AddDays(-3); // game already finished
        var endsAtUtc = DateTime.UtcNow.AddDays(-1);
        var gameName = $"Feedback{submissionPrefix}E2E {Guid.NewGuid():N}".Substring(0, 24);

        // Single-question kid schema — exercises the same parser path as the
        // canonical seed without dragging the full 7-question payload through
        // the test. See SeedFeedbackQuestions2026 for the production shape.
        const string KidsJson =
            """[{"key":"strongest_moment","label":"Nejsilnější zážitek","helpText":"Krátké věty stačí.","placeholders":[]}]""";

        var game = new Game
        {
            Name = gameName,
            Description = "E2E feedback",
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = endsAtUtc,
            RegistrationClosesAtUtc = startsAtUtc.AddDays(-7),
            MealOrderingClosesAtUtc = startsAtUtc.AddDays(-10),
            PaymentDueAtUtc = startsAtUtc.AddDays(-5),
            BankAccount = "CZ6508000000192000145399",
            BankAccountName = "Ovčina z.s.",
            VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
            TargetPlayerCountTotal = 80,
            IsPublished = true,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            FeedbackKidQuestionsJson = KidsJson,
            FeedbackOpensAtUtc = startsAtUtc,
            FeedbackClosesAtUtc = startsAtUtc.AddMonths(2),
        };

        var person = new Person
        {
            FirstName = "Anežka",
            LastName = "FeedbackOvá",
            BirthYear = 2014,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        var submission = new RegistrationSubmission
        {
            Game = game,
            RegistrantUserId = registrant.Id,
            PrimaryContactName = "Rodina FeedbackTest",
            PrimaryEmail = "feedbacktest@example.cz",
            PrimaryPhone = "+420777987654",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = nowUtc,
            LastEditedAtUtc = nowUtc,
            ExpectedTotalAmount = 0m
        };

        var registration = new Registration
        {
            Submission = submission,
            Person = person,
            AttendeeType = AttendeeType.Player,
            Status = RegistrationStatus.Active,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        db.Add(game);
        db.AddRange(submission, person, registration);
        await db.SaveChangesAsync();

        return new SeededFeedback(
            game.Id,
            gameName,
            submission.Id,
            registration.Id);
    }

    /// <summary>
    /// Mints a feedback token for a registration. The host runs as a separate
    /// process so we can't resolve <see cref="FeedbackTokenService"/> from DI;
    /// instead we set the token column directly to a fresh GUID — same shape
    /// as <c>EnsureTokenAsync</c>.
    /// </summary>
    private async Task<Guid> EnsureTokenAsync(int registrationId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var db = new ApplicationDbContext(options);
        var registration = await db.Registrations.SingleAsync(r => r.Id == registrationId);
        if (registration.FeedbackToken is null)
        {
            registration.FeedbackToken = Guid.NewGuid();
            await db.SaveChangesAsync();
        }
        return registration.FeedbackToken.Value;
    }

    /// <summary>
    /// Ensures the seeded test registrant has a linked <see cref="Person"/>.
    /// The dev-data seeder does NOT auto-link one (registrants exist purely
    /// as identity rows there), but the scribe audit pipeline writes
    /// <c>LastEditedByPersonId</c>, so we lazily provision a Person on the
    /// first run of this test and reuse it after.
    /// </summary>
    private async Task<int> EnsureRegistrantPersonIdAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var db = new ApplicationDbContext(options);
        var user = await db.Users.SingleAsync(x => x.Email == RegistrantEmail);
        if (user.PersonId is { } existing)
        {
            return existing;
        }

        var nowUtc = DateTime.UtcNow;
        var person = new Person
        {
            FirstName = "Ukázkový",
            LastName = "Registrující",
            BirthYear = 1985,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
        db.People.Add(person);
        await db.SaveChangesAsync();

        user.PersonId = person.Id;
        await db.SaveChangesAsync();
        return person.Id;
    }

    private async Task<(string AnswersJson, FeedbackEditedBy LastEditedBy, DateTimeOffset? SubmittedAtUtc)>
        ReadResponseAsync(int registrationId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var db = new ApplicationDbContext(options);
        var response = await db.FeedbackResponses
            .AsNoTracking()
            .SingleAsync(r => r.RegistrationId == registrationId);
        return (response.AnswersJson, response.LastEditedBy, response.SubmittedAtUtc);
    }

    private async Task<int?> ReadLastEditedByPersonIdAsync(int registrationId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var db = new ApplicationDbContext(options);
        var response = await db.FeedbackResponses
            .AsNoTracking()
            .SingleAsync(r => r.RegistrationId == registrationId);
        return response.LastEditedByPersonId;
    }

    private async Task LoginAsync(IPage page, string email)
    {
        await page.GotoAsync(
            $"{_fixture.BaseUrl}/testing/login?email={Uri.EscapeDataString(email)}&returnUrl=%2F",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    private static async Task WaitForInteractiveReadyAsync(IPage page)
    {
        await page.GetByTestId("interactive-ready").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 5000
        });

        var overlay = page.Locator(".announcement-overlay");
        if (await overlay.IsVisibleAsync())
        {
            await overlay.GetByText("Pokračovat").ClickAsync();
            await overlay.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Hidden,
                Timeout = 3000
            });
        }
    }

    private async Task AssertNoBlazorErrorsAsync(IPage page)
    {
        var errorUi = page.Locator("#blazor-error-ui");
        if (await errorUi.IsVisibleAsync())
        {
            var text = await errorUi.InnerTextAsync();
            var bodyText = await page.Locator("body").InnerTextAsync();
            throw new XunitException(
                $"Blazor error UI was visible: {text}\n"
                + $"Page body:\n{bodyText}\n"
                + $"Host diagnostics:\n{_fixture.GetDiagnostics()}");
        }
    }

    private sealed record SeededFeedback(
        int GameId,
        string GameName,
        int SubmissionId,
        int RegistrationId);
}
