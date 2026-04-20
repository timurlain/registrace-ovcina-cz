using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using RegistraceOvcina.Web.Data;
using Xunit.Sdk;

namespace RegistraceOvcina.E2E;

/// <summary>
/// End-to-end smoke test for the Character Prep feature (Phase 9a).
///
/// Golden path:
///   1. Seed a game, a RegistrationSubmission with 2 Player attendees, 5 equipment options.
///   2. Organizer logs in, opens the prep dashboard.
///   3. Because the test host uses UnconfiguredCharacterPrepEmailSender (no Graph mailbox
///      configured in Testing env), the "Poslat pozvánku" button would throw on send.
///      Instead we directly mark the submission as invited in the DB — simulates a successful
///      bulk send — and generate the prep token (same crypto shape the token service uses).
///      This matches the "fallback" path documented in the Phase 9a task description.
///   4. Parent opens /postavy/{token} in a fresh (anonymous) browser context, fills names and
///      picks equipment for both rows, saves.
///   5. Reload confirms pre-fill persistence.
///   6. Organizer reloads dashboard; stats show 1 FullyFilled / 0 Pending and each row says
///      "Hotovo".
/// </summary>
public sealed class CharacterPrepSmokeTests : IClassFixture<AppFixture>
{
    private const string AdminEmail = "admin@ovcina.test";
    private const string RegistrantEmail = "registrant@ovcina.test";

    private readonly AppFixture _fixture;

    public CharacterPrepSmokeTests(AppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CharacterPrep_GoldenPath_EndToEnd()
    {
        var seeded = await SeedCharacterPrepAsync();

        // ───── 1. Organizer opens the dashboard and sees 0 invited, 0 FullyFilled, 1 Pending ─────
        var organizerPage = await _fixture.Browser.NewPageAsync();
        await LoginAsync(organizerPage, AdminEmail);
        await organizerPage.GotoAsync(
            $"{_fixture.BaseUrl}/organizace/hry/{seeded.GameId}/priprava-postav",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForInteractiveReadyAsync(organizerPage);

        await organizerPage.GetByTestId("prep-stat-total-households").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 5000 });
        Assert.Equal("1",
            (await organizerPage.GetByTestId("prep-stat-total-households").TextContentAsync())?.Trim());
        Assert.Equal("0",
            (await organizerPage.GetByTestId("prep-stat-invited").TextContentAsync())?.Trim());
        Assert.Equal("0",
            (await organizerPage.GetByTestId("prep-stat-fully-filled").TextContentAsync())?.Trim());

        await AssertNoBlazorErrorsAsync(organizerPage);

        // ───── 2. Simulate "Poslat pozvánku" bulk send ─────
        // The Testing environment doesn't have Microsoft Graph configured, so the registered
        // ICharacterPrepEmailSender is UnconfiguredCharacterPrepEmailSender which would throw.
        // The host is a separate process (not WebApplicationFactory), so we can't swap the DI
        // registration at test time. Fallback pattern: stamp the invited timestamp directly and
        // mint a prep token via the same token service the production code uses.
        var token = await MarkInvitedAndGenerateTokenAsync(seeded.SubmissionId);

        // ───── 3. Organizer reloads — Invited should now be 1 ─────
        await organizerPage.ReloadAsync(new PageReloadOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });
        await WaitForInteractiveReadyAsync(organizerPage);

        Assert.Equal("1",
            (await organizerPage.GetByTestId("prep-stat-invited").TextContentAsync())?.Trim());

        // ───── 4. Parent opens /postavy/{token} in a fresh anonymous context ─────
        // New browser context = no admin cookie, exactly like the parent in real life.
        await using var parentContext = await _fixture.Browser.NewContextAsync();
        var parentPage = await parentContext.NewPageAsync();

        await parentPage.GotoAsync(
            $"{_fixture.BaseUrl}/postavy/{token}",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForInteractiveReadyAsync(parentPage);

        // Both cards visible
        var cardOne = parentPage.GetByTestId($"prep-card-{seeded.RegistrationIdOne}");
        var cardTwo = parentPage.GetByTestId($"prep-card-{seeded.RegistrationIdTwo}");
        await cardOne.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await cardTwo.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        // Fill child one
        var nameOne = parentPage.GetByTestId($"character-name-{seeded.RegistrationIdOne}");
        await FillAndCommitAsync(nameOne, "Aragorn");
        await parentPage.Locator(
            $"#equip-{seeded.RegistrationIdOne}-{seeded.EquipmentIds[0]}").CheckAsync();

        // Fill child two
        var nameTwo = parentPage.GetByTestId($"character-name-{seeded.RegistrationIdTwo}");
        await FillAndCommitAsync(nameTwo, "Legolas");
        await parentPage.Locator(
            $"#equip-{seeded.RegistrationIdTwo}-{seeded.EquipmentIds[1]}").CheckAsync();

        // Save
        await parentPage.GetByTestId("character-prep-save").ClickAsync();

        var statusLocator = parentPage.GetByTestId("character-prep-status");
        try
        {
            await statusLocator.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        }
        catch (TimeoutException)
        {
            var bodyText = await parentPage.Locator("body").InnerTextAsync();
            throw new XunitException(
                $"Character prep save did not produce a status message. Page body:{Environment.NewLine}{bodyText}");
        }

        var statusText = (await statusLocator.TextContentAsync())?.Trim();
        Assert.Equal("Uloženo, díky.", statusText);

        await AssertNoBlazorErrorsAsync(parentPage);

        // ───── 5. Reload parent page — values pre-filled ─────
        await parentPage.ReloadAsync(new PageReloadOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });
        await WaitForInteractiveReadyAsync(parentPage);

        Assert.Equal("Aragorn",
            await parentPage.GetByTestId($"character-name-{seeded.RegistrationIdOne}").InputValueAsync());
        Assert.Equal("Legolas",
            await parentPage.GetByTestId($"character-name-{seeded.RegistrationIdTwo}").InputValueAsync());

        var firstRadio = parentPage.Locator(
            $"#equip-{seeded.RegistrationIdOne}-{seeded.EquipmentIds[0]}");
        var secondRadio = parentPage.Locator(
            $"#equip-{seeded.RegistrationIdTwo}-{seeded.EquipmentIds[1]}");
        Assert.True(await firstRadio.IsCheckedAsync(),
            "First child's equipment radio should be pre-filled after reload.");
        Assert.True(await secondRadio.IsCheckedAsync(),
            "Second child's equipment radio should be pre-filled after reload.");

        await parentPage.CloseAsync();

        // ───── 6. Organizer reloads dashboard — both rows Hotovo, 1 FullyFilled / 0 Pending ─────
        await organizerPage.ReloadAsync(new PageReloadOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });
        await WaitForInteractiveReadyAsync(organizerPage);

        Assert.Equal("1",
            (await organizerPage.GetByTestId("prep-stat-fully-filled").TextContentAsync())?.Trim());
        Assert.Equal("0",
            (await organizerPage.GetByTestId("prep-stat-pending").TextContentAsync())?.Trim());

        var statusBadgeOne = organizerPage.GetByTestId($"prep-status-{seeded.RegistrationIdOne}");
        var statusBadgeTwo = organizerPage.GetByTestId($"prep-status-{seeded.RegistrationIdTwo}");
        await statusBadgeOne.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await statusBadgeTwo.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        Assert.Equal("Hotovo", (await statusBadgeOne.TextContentAsync())?.Trim());
        Assert.Equal("Hotovo", (await statusBadgeTwo.TextContentAsync())?.Trim());

        await AssertNoBlazorErrorsAsync(organizerPage);
        await organizerPage.CloseAsync();
    }

    private async Task<SeededCharacterPrepData> SeedCharacterPrepAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var db = new ApplicationDbContext(options);
        var registrant = await db.Users.SingleAsync(x => x.Email == RegistrantEmail);
        var nowUtc = DateTime.UtcNow;
        var startsAtUtc = DateTime.UtcNow.AddMonths(2);
        var endsAtUtc = startsAtUtc.AddDays(2);
        var gameName = $"PrepE2E {Guid.NewGuid():N}".Substring(0, 18);

        var game = new Game
        {
            Name = gameName,
            Description = "E2E character prep",
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
            UpdatedAtUtc = nowUtc
        };

        var equipmentSpecs = new (string Key, string DisplayName, string Description, int SortOrder)[]
        {
            ("tesak", "Tesák (3/1)", "Útok 3, obrana 1", 1),
            ("dlouhy-nuz", "Dlouhý nůž (2/2)", "Útok 2, obrana 2", 2),
            ("vrhaci-nuz", "Vrhací nůž (3/0)", "Útok 3, obrana 0", 3),
            ("dyka-svitky", "Dýka (1/2), 4 svitky kouzel", "Útok 1, obrana 2 + 4 svitky kouzel", 4),
            ("mince", "5 měďáků / grošů", "Žádná zbraň, 5 mincí do začátku", 5),
        };

        var equipmentOptions = equipmentSpecs.Select(spec => new StartingEquipmentOption
        {
            Game = game,
            Key = spec.Key,
            DisplayName = spec.DisplayName,
            Description = spec.Description,
            SortOrder = spec.SortOrder
        }).ToList();

        var submission = new RegistrationSubmission
        {
            Game = game,
            RegistrantUserId = registrant.Id,
            PrimaryContactName = "Rodina PrepTest",
            PrimaryEmail = "preptest@example.cz",
            PrimaryPhone = "+420777123456",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = nowUtc,
            LastEditedAtUtc = nowUtc,
            ExpectedTotalAmount = 0m
        };

        var personOne = new Person
        {
            FirstName = "Anna",
            LastName = "PrepOvá",
            BirthYear = 2014,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
        var personTwo = new Person
        {
            FirstName = "Petr",
            LastName = "Prep",
            BirthYear = 2012,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        var registrationOne = new Registration
        {
            Submission = submission,
            Person = personOne,
            AttendeeType = AttendeeType.Player,
            Status = RegistrationStatus.Active,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
        var registrationTwo = new Registration
        {
            Submission = submission,
            Person = personTwo,
            AttendeeType = AttendeeType.Player,
            Status = RegistrationStatus.Active,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        db.Add(game);
        db.AddRange(equipmentOptions);
        db.AddRange(submission, personOne, personTwo, registrationOne, registrationTwo);
        await db.SaveChangesAsync();

        return new SeededCharacterPrepData(
            game.Id,
            submission.Id,
            registrationOne.Id,
            registrationTwo.Id,
            equipmentOptions.Select(x => x.Id).ToArray());
    }

    private async Task<string> MarkInvitedAndGenerateTokenAsync(int submissionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var db = new ApplicationDbContext(options);
        var submission = await db.RegistrationSubmissions.SingleAsync(x => x.Id == submissionId);
        submission.CharacterPrepInvitedAtUtc = DateTimeOffset.UtcNow;
        if (string.IsNullOrEmpty(submission.CharacterPrepToken))
        {
            // Must match CharacterPrepTokenService generator semantics (URL-safe random string).
            submission.CharacterPrepToken = GenerateToken();
        }
        await db.SaveChangesAsync();
        return submission.CharacterPrepToken!;
    }

    /// <summary>
    /// Mirrors CharacterPrepTokenService's token shape: 32 bytes of crypto randomness,
    /// base64url encoded. Using our own generator (rather than resolving the service) keeps the
    /// test host process loosely coupled — we only touch DB.
    /// </summary>
    private static string GenerateToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private async Task LoginAsync(IPage page, string email)
    {
        await page.GotoAsync(
            $"{_fixture.BaseUrl}/testing/login?email={Uri.EscapeDataString(email)}&returnUrl=%2F",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    private static async Task FillAndCommitAsync(ILocator locator, string value)
    {
        await locator.FillAsync(value);
        await locator.DispatchEventAsync("change");
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
                $"Blazor error UI was visible: {text}{Environment.NewLine}"
                + $"Page body:{Environment.NewLine}{bodyText}{Environment.NewLine}"
                + $"Host diagnostics:{Environment.NewLine}{_fixture.GetDiagnostics()}");
        }
    }

    private sealed record SeededCharacterPrepData(
        int GameId,
        int SubmissionId,
        int RegistrationIdOne,
        int RegistrationIdTwo,
        int[] EquipmentIds);
}
