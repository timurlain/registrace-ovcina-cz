using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using RegistraceOvcina.Web.Data;
using Xunit.Sdk;

namespace RegistraceOvcina.E2E;

public sealed class SmokeTests : IClassFixture<AppFixture>
{
    private const string AdminEmail = "admin@ovcina.test";
    private const string RegistrantEmail = "registrant@ovcina.test";

    private readonly AppFixture _fixture;

    public SmokeTests(AppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PublicLoginLink_OpensLoginPage()
    {
        var page = await _fixture.Browser.NewPageAsync();

        await page.GotoAsync(_fixture.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });
        await WaitForInteractiveReadyAsync(page);

        await Task.WhenAll(
            page.WaitForURLAsync("**/Account/Login", new PageWaitForURLOptions
            {
                Timeout = 5000
            }),
            page.GetByTestId("home-login-button").ClickAsync());

        await page.GetByTestId("login-email").WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 5000
        });

        await page.CloseAsync();
    }

    [Fact]
    public async Task AdminLogin_AllowsAccessToGameManagement()
    {
        var adminPage = await _fixture.Browser.NewPageAsync();

        await LoginAsync(adminPage, AdminEmail);
        await adminPage.GotoAsync($"{_fixture.BaseUrl}/admin/hry");
        await WaitForInteractiveReadyAsync(adminPage);

        await adminPage.GetByTestId("game-name").WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 5000
        });
        await adminPage.GetByTestId("create-game-submit").WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 5000
        });

        await AssertNoBlazorErrorsAsync(adminPage);
        await adminPage.CloseAsync();
    }

    [Fact]
    public async Task AdminCanManageOrganizerAndAdminRoles()
    {
        var adminPage = await _fixture.Browser.NewPageAsync();

        await LoginAsync(adminPage, AdminEmail);
        await adminPage.GotoAsync($"{_fixture.BaseUrl}/admin/organizatori", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });
        await WaitForInteractiveReadyAsync(adminPage);

        await adminPage.GetByTestId("user-management-title").WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 5000
        });

        await ToggleUserManagementActionAsync(
            adminPage,
            RegistrantEmail,
            "Přidat organizátora",
            "organizer-added",
            "Role organizátora byla přidána.");

        await ToggleUserManagementActionAsync(
            adminPage,
            RegistrantEmail,
            "Přidat správce",
            "admin-added",
            "Role správce byla přidána.");

        var managedRow = GetUserRow(adminPage, RegistrantEmail);
        await managedRow.GetByRole(AriaRole.Button, new() { Name = "Odebrat organizátora" }).WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 5000
        });
        await managedRow.GetByRole(AriaRole.Button, new() { Name = "Odebrat správce" }).WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 5000
        });

        await AssertNoBlazorErrorsAsync(adminPage);
        await adminPage.CloseAsync();
    }

    [Fact]
    public async Task AdminCanOpenFoodSummaryAndSeeAggregatedCounts()
    {
        var seeded = await SeedFoodSummaryAsync();
        var adminPage = await _fixture.Browser.NewPageAsync();

        await LoginAsync(adminPage, AdminEmail);
        await adminPage.GotoAsync($"{_fixture.BaseUrl}/organizace/strava?gameId={seeded.GameId}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });
        await WaitForInteractiveReadyAsync(adminPage);

        await adminPage.GetByTestId("food-summary-game-title").WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 5000
        });

        Assert.Equal(seeded.GameName, (await adminPage.GetByTestId("food-summary-game-title").TextContentAsync())?.Trim());
        Assert.Equal("4", (await adminPage.GetByTestId("food-summary-total-selections").TextContentAsync())?.Trim());
        Assert.Equal("2", (await adminPage.GetByTestId("food-summary-registration-count").TextContentAsync())?.Trim());
        Assert.Equal("2", (await adminPage.GetByTestId($"food-count-{seeded.DayOne:yyyyMMdd}-{seeded.SoupOptionId}").TextContentAsync())?.Trim());
        Assert.Equal("1", (await adminPage.GetByTestId($"food-count-{seeded.DayOne:yyyyMMdd}-{seeded.LunchOptionId}").TextContentAsync())?.Trim());
        Assert.Equal("1", (await adminPage.GetByTestId($"food-count-{seeded.DayTwo:yyyyMMdd}-{seeded.LunchOptionId}").TextContentAsync())?.Trim());
        Assert.Equal("0", (await adminPage.GetByTestId($"food-count-{seeded.DayTwo:yyyyMMdd}-{seeded.SoupOptionId}").TextContentAsync())?.Trim());

        await AssertNoBlazorErrorsAsync(adminPage);
        await adminPage.CloseAsync();
    }

    [Fact]
    public async Task AdminAndRegistrantSmokeFlow_WorksEndToEnd()
    {
        var adminPage = await _fixture.Browser.NewPageAsync();
        var gameName = $"Smoke hra {DateTime.UtcNow:yyyyMMddHHmmss}";
        var gameStartsAt = DateTime.Today.AddMonths(1).AddHours(17);

        await LoginAsync(adminPage, AdminEmail);
        await adminPage.GotoAsync($"{_fixture.BaseUrl}/admin/hry");
        await WaitForInteractiveReadyAsync(adminPage);

        await FillAndCommitAsync(adminPage.GetByTestId("game-name"), gameName);
        await FillAndCommitAsync(adminPage.GetByTestId("game-starts"), gameStartsAt.ToString("yyyy-MM-ddTHH:mm"));
        await FillAndCommitAsync(adminPage.GetByTestId("game-ends"), gameStartsAt.AddDays(2).ToString("yyyy-MM-ddTHH:mm"));
        await FillAndCommitAsync(adminPage.GetByLabel("Uzávěrka registrace"), gameStartsAt.AddDays(-7).ToString("yyyy-MM-ddTHH:mm"));
        await FillAndCommitAsync(adminPage.GetByLabel("Uzávěrka jídel"), gameStartsAt.AddDays(-10).ToString("yyyy-MM-ddTHH:mm"));
        await FillAndCommitAsync(adminPage.GetByLabel("Termín platby"), gameStartsAt.AddDays(-5).ToString("yyyy-MM-ddTHH:mm"));
        await adminPage.GetByTestId("create-game-submit").ClickAsync();

        try
        {
            await adminPage.GetByText(gameName).WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });
        }
        catch (TimeoutException)
        {
            var errorAlert = adminPage.Locator(".alert-danger");
            if (await errorAlert.IsVisibleAsync())
            {
                throw new XunitException($"Game creation failed: {await errorAlert.InnerTextAsync()}");
            }

            var bodyText = await adminPage.Locator("body").InnerTextAsync();
            throw new XunitException($"Game creation did not complete. Page body:{Environment.NewLine}{bodyText}");
        }

        await AssertNoBlazorErrorsAsync(adminPage);
        await adminPage.CloseAsync();

        var registrantPage = await _fixture.Browser.NewPageAsync();

        await LoginAsync(registrantPage, RegistrantEmail);
        await registrantPage.GotoAsync($"{_fixture.BaseUrl}/moje-prihlasky");
        await WaitForInteractiveReadyAsync(registrantPage);

        try
        {
            await registrantPage.GetByText(gameName).WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });
        }
        catch (TimeoutException)
        {
            var bodyText = await registrantPage.Locator("body").InnerTextAsync();
            throw new XunitException($"Created game was not visible to registrant. Page body:{Environment.NewLine}{bodyText}");
        }

        await registrantPage.Locator("[data-testid^='create-draft-']").First.ClickAsync();

        await registrantPage.WaitForURLAsync("**/prihlasky/*");
        await WaitForInteractiveReadyAsync(registrantPage);
        await FillAndCommitAsync(registrantPage.GetByTestId("contact-name"), "Jan Smok");
        await FillAndCommitAsync(registrantPage.GetByTestId("group-name"), "Rodina Smokova");
        await FillAndCommitAsync(registrantPage.GetByTestId("contact-email"), "rodina@example.cz");
        await FillAndCommitAsync(registrantPage.GetByLabel("Kontaktní telefon"), "+420777123456");
        await registrantPage.GetByRole(AriaRole.Button, new() { Name = "Uložit kontakt" }).ClickAsync();
        try
        {
            await registrantPage.GetByText("Kontaktní údaje byly uložené.").WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });
        }
        catch (TimeoutException)
        {
            var bodyText = await registrantPage.Locator("body").InnerTextAsync();
            throw new XunitException($"Contact save did not complete. Page body:{Environment.NewLine}{bodyText}");
        }

        await WaitForInteractiveReadyAsync(registrantPage);

        await registrantPage.GetByTestId("attendee-first-name").FillAsync("Anna");
        await registrantPage.GetByTestId("attendee-last-name").FillAsync("Smoková");
        await registrantPage.GetByTestId("attendee-birth-year").FillAsync("2014");
        await registrantPage.GetByTestId("type-player").CheckAsync();
        await WaitForInteractiveReadyAsync(registrantPage);
        await registrantPage.Locator("#pst-independent").ClickAsync(new LocatorClickOptions { Force = true });
        await registrantPage.Locator("#pst-independent").DispatchEventAsync("change", new { bubbles = true });
        await registrantPage.Locator("#attendee-phone").FillAsync("+420777999888");
        await registrantPage.Locator("#attendee-phone").BlurAsync();
        await registrantPage.Locator("#guardian-name").FillAsync("Tomáš Smok");
        await registrantPage.Locator("#guardian-name").BlurAsync();
        await registrantPage.Locator("#guardian-relationship").FillAsync("otec");
        await registrantPage.Locator("#guardian-relationship").BlurAsync();
        await registrantPage.Locator("#guardian-confirmed").CheckAsync();

        // Select lodging (#70 regression test)
        await registrantPage.Locator("#lodge-tent").CheckAsync();

        // Select food if meal options are visible (#70 regression test)
        var firstMealRadio = registrantPage.Locator("input[type='radio'][name^='MealSelections']").First;
        if (await firstMealRadio.IsVisibleAsync())
        {
            await firstMealRadio.CheckAsync();
        }

        await registrantPage.GetByTestId("add-attendee-submit").ClickAsync();

        try
        {
            await registrantPage.Locator("[data-testid^='attendee-card-']").First.WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });
            await registrantPage.GetByText("Účastník byl přidán.").WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });
        }
        catch (TimeoutException)
        {
            var bodyText = await registrantPage.Locator("body").InnerTextAsync();
            throw new XunitException($"Attendee add did not complete. Page body:{Environment.NewLine}{bodyText}");
        }

        // Verify phone number preserved with + prefix (#73)
        var pageBody = await registrantPage.Locator("body").InnerTextAsync();
        Assert.Contains("+420777999888", pageBody);

        // Verify lodging was saved (#70)
        Assert.Contains("Vlastní stan", pageBody);

        await WaitForInteractiveReadyAsync(registrantPage);

        // The submission may already be submitted if another test in the suite
        // used the same game+user combo. Handle both cases gracefully.
        var submitButton = registrantPage.GetByTestId("submit-submission");
        if (await submitButton.IsVisibleAsync())
        {
            await submitButton.ScrollIntoViewIfNeededAsync();
            await submitButton.ClickAsync();

            try
            {
                await registrantPage.GetByText("Přihláška byla odeslaná.").WaitForAsync(new LocatorWaitForOptions
                {
                    Timeout = 5000
                });
            }
            catch (TimeoutException)
            {
                var bodyText = await registrantPage.Locator("body").InnerTextAsync();
                throw new XunitException($"Submission did not complete. Page body:{Environment.NewLine}{bodyText}");
            }
        }

        // Verify submission is in submitted state — payment info visible
        var isSubmitted = await registrantPage.GetByText("Zaplaťte prosím převodem").IsVisibleAsync()
            || await registrantPage.GetByText("nemá žádnou platbu").IsVisibleAsync()
            || await registrantPage.GetByTestId("payment-qr").IsVisibleAsync();
        Assert.True(isSubmitted, "Submission should be in submitted state with payment info visible");

        await registrantPage.GetByTestId("submission-total").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });

        var totalText = await registrantPage.GetByTestId("submission-total").TextContentAsync();
        Assert.NotNull(totalText);
        Assert.Contains("Kč", totalText);
        // Total includes player base price + any food orders selected during registration
        var numericPart = totalText.Trim().Replace(" Kč", "").Replace(",", ".");
        Assert.True(decimal.TryParse(numericPart, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var total));
        Assert.True(total >= 100m, $"Expected total >= 100 Kč (player base price), got {total}");
        await AssertNoBlazorErrorsAsync(registrantPage);
        await registrantPage.CloseAsync();
    }

    private async Task LoginAsync(IPage page, string email)
    {
        await page.GotoAsync($"{_fixture.BaseUrl}/testing/login?email={Uri.EscapeDataString(email)}&returnUrl=%2F", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    private static ILocator GetUserRow(IPage page, string email) =>
        page.GetByText(email).Locator("xpath=ancestor::tr");

    private async Task ToggleUserManagementActionAsync(
        IPage page,
        string email,
        string buttonText,
        string expectedStatusCode,
        string expectedMessage)
    {
        var row = GetUserRow(page, email);

        await Task.WhenAll(
            page.WaitForURLAsync($"**/admin/organizatori?status={expectedStatusCode}", new PageWaitForURLOptions
            {
                Timeout = 5000
            }),
            row.GetByRole(AriaRole.Button, new() { Name = buttonText }).ClickAsync());

        await WaitForInteractiveReadyAsync(page);
        await page.GetByText(expectedMessage).WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 5000
        });
    }

    private async Task<SeededFoodSummaryData> SeedFoodSummaryAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var db = new ApplicationDbContext(options);
        var registrant = await db.Users.SingleAsync(x => x.Email == RegistrantEmail);
        var admin = await db.Users.SingleAsync(x => x.Email == AdminEmail);
        var nowUtc = DateTime.UtcNow;
        var startsAtUtc = new DateTime(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc);
        var endsAtUtc = startsAtUtc.AddDays(1).AddHours(8);
        var gameName = $"Jidlo {Guid.NewGuid():N}".Substring(0, 18);

        var game = new Game
        {
            Name = gameName,
            Description = "E2E souhrn stravy",
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

        var soup = new MealOption { Game = game, Name = "Polévka", Price = 85m, IsActive = true };
        var lunch = new MealOption { Game = game, Name = "Oběd", Price = 120m, IsActive = true };

        var submitted = new RegistrationSubmission
        {
            Game = game,
            RegistrantUserId = registrant.Id,
            PrimaryContactName = "Rodina Testova",
            PrimaryEmail = "rodina@example.cz",
            PrimaryPhone = "+420777123456",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = nowUtc,
            LastEditedAtUtc = nowUtc,
            ExpectedTotalAmount = 0m
        };

        var draft = new RegistrationSubmission
        {
            Game = game,
            RegistrantUserId = admin.Id,
            PrimaryContactName = "Rodina Draft",
            PrimaryEmail = "draft@example.cz",
            PrimaryPhone = "+420777000000",
            Status = SubmissionStatus.Draft,
            LastEditedAtUtc = nowUtc,
            ExpectedTotalAmount = 0m
        };

        var personOne = new Person { FirstName = "Anna", LastName = "Testová", BirthYear = 2014, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc };
        var personTwo = new Person { FirstName = "Petr", LastName = "Test", BirthYear = 2012, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc };
        var personThree = new Person { FirstName = "Eva", LastName = "Zrusena", BirthYear = 2010, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc };
        var personFour = new Person { FirstName = "Lucie", LastName = "Draftova", BirthYear = 2016, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc };

        var activeRegistrationOne = new Registration
        {
            Submission = submitted,
            Person = personOne,
            AttendeeType = AttendeeType.Player,
            Status = RegistrationStatus.Active,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        var activeRegistrationTwo = new Registration
        {
            Submission = submitted,
            Person = personTwo,
            AttendeeType = AttendeeType.Player,
            Status = RegistrationStatus.Active,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        var cancelledRegistration = new Registration
        {
            Submission = submitted,
            Person = personThree,
            AttendeeType = AttendeeType.Player,
            Status = RegistrationStatus.Cancelled,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        var draftRegistration = new Registration
        {
            Submission = draft,
            Person = personFour,
            AttendeeType = AttendeeType.Player,
            Status = RegistrationStatus.Active,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        db.AddRange(game, soup, lunch, submitted, draft, personOne, personTwo, personThree, personFour);
        db.AddRange(activeRegistrationOne, activeRegistrationTwo, cancelledRegistration, draftRegistration);
        await db.SaveChangesAsync();

        db.FoodOrders.AddRange(
            new FoodOrder { RegistrationId = activeRegistrationOne.Id, MealOptionId = soup.Id, MealDayUtc = startsAtUtc.Date, Price = soup.Price },
            new FoodOrder { RegistrationId = activeRegistrationOne.Id, MealOptionId = lunch.Id, MealDayUtc = startsAtUtc.Date, Price = lunch.Price },
            new FoodOrder { RegistrationId = activeRegistrationTwo.Id, MealOptionId = soup.Id, MealDayUtc = startsAtUtc.Date, Price = soup.Price },
            new FoodOrder { RegistrationId = activeRegistrationTwo.Id, MealOptionId = lunch.Id, MealDayUtc = startsAtUtc.Date.AddDays(1), Price = lunch.Price },
            new FoodOrder { RegistrationId = cancelledRegistration.Id, MealOptionId = soup.Id, MealDayUtc = startsAtUtc.Date, Price = soup.Price },
            new FoodOrder { RegistrationId = draftRegistration.Id, MealOptionId = lunch.Id, MealDayUtc = startsAtUtc.Date, Price = lunch.Price });

        await db.SaveChangesAsync();

        return new SeededFoodSummaryData(game.Id, gameName, startsAtUtc.Date, startsAtUtc.Date.AddDays(1), soup.Id, lunch.Id);
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

        // Dismiss announcement overlay if present
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

    [Fact]
    public async Task AddAdultAttendee_WithRole_Succeeds()
    {
        var page = await _fixture.Browser.NewPageAsync();
        await LoginAsync(page, RegistrantEmail);

        // Navigate to existing draft or create one
        await page.GotoAsync($"{_fixture.BaseUrl}/moje-prihlasky");
        await WaitForInteractiveReadyAsync(page);

        // Click first available draft or create new
        var existingDraft = page.Locator("[data-testid^='open-submission-']").First;
        if (await existingDraft.IsVisibleAsync())
        {
            await existingDraft.ClickAsync();
        }
        else
        {
            await page.Locator("[data-testid^='create-draft-']").First.ClickAsync();
        }

        await page.WaitForURLAsync("**/prihlasky/*");
        await WaitForInteractiveReadyAsync(page);

        // Fill adult attendee
        await page.GetByTestId("attendee-first-name").FillAsync("Tomáš");
        await page.GetByTestId("attendee-last-name").FillAsync("Pajonk");
        await page.GetByTestId("attendee-birth-year").FillAsync("1985");
        await page.GetByTestId("type-adult").ClickAsync();

        // Select an adult role
        await page.Locator("#ar-tech").ClickAsync();

        await page.GetByTestId("add-attendee-submit").ClickAsync();

        try
        {
            await page.Locator("[data-testid^='attendee-card-']").Last.WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });
            await page.GetByText("Účastník byl přidán.").WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });
        }
        catch (TimeoutException)
        {
            var bodyText = await page.Locator("body").InnerTextAsync();
            throw new XunitException($"Adult attendee add failed. Page body:{Environment.NewLine}{bodyText}");
        }

        // Verify the attendee card shows "Dospělý"
        var lastCard = page.Locator("[data-testid^='attendee-card-']").Last;
        var cardText = await lastCard.InnerTextAsync();
        Assert.Contains("Tomáš Pajonk", cardText);
        Assert.Contains("Dospělý", cardText);

        await AssertNoBlazorErrorsAsync(page);
        await page.CloseAsync();
    }

    [Fact]
    public async Task AddAdultAttendee_WithoutRole_ShowsValidationError()
    {
        var page = await _fixture.Browser.NewPageAsync();
        await LoginAsync(page, RegistrantEmail);

        await page.GotoAsync($"{_fixture.BaseUrl}/moje-prihlasky");
        await WaitForInteractiveReadyAsync(page);

        var existingDraft = page.Locator("[data-testid^='open-submission-']").First;
        if (await existingDraft.IsVisibleAsync())
        {
            await existingDraft.ClickAsync();
        }
        else
        {
            await page.Locator("[data-testid^='create-draft-']").First.ClickAsync();
        }

        await page.WaitForURLAsync("**/prihlasky/*");
        await WaitForInteractiveReadyAsync(page);

        // Fill adult attendee WITHOUT selecting any role
        await page.GetByTestId("attendee-first-name").FillAsync("Jana");
        await page.GetByTestId("attendee-last-name").FillAsync("Nováková");
        await page.GetByTestId("attendee-birth-year").FillAsync("1980");
        await page.GetByTestId("type-adult").ClickAsync();

        // Do NOT check any adult role checkbox
        await page.GetByTestId("add-attendee-submit").ClickAsync();

        // Validation error should appear, form should NOT reset
        try
        {
            await page.GetByText("Vyberte alespoň jednu roli dospělého").WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });
        }
        catch (TimeoutException)
        {
            var bodyText = await page.Locator("body").InnerTextAsync();
            throw new XunitException($"Validation error for missing adult role not shown. Page body:{Environment.NewLine}{bodyText}");
        }

        // Verify form fields are preserved (not reset)
        var firstName = await page.GetByTestId("attendee-first-name").InputValueAsync();
        Assert.Equal("Jana", firstName);

        var lastName = await page.GetByTestId("attendee-last-name").InputValueAsync();
        Assert.Equal("Nováková", lastName);

        await AssertNoBlazorErrorsAsync(page);
        await page.CloseAsync();
    }

    [Fact]
    public async Task AddAdultAttendee_MultipleRoles_AllSaved()
    {
        var page = await _fixture.Browser.NewPageAsync();
        await LoginAsync(page, RegistrantEmail);

        await page.GotoAsync($"{_fixture.BaseUrl}/moje-prihlasky");
        await WaitForInteractiveReadyAsync(page);

        var existingDraft = page.Locator("[data-testid^='open-submission-']").First;
        if (await existingDraft.IsVisibleAsync())
        {
            await existingDraft.ClickAsync();
        }
        else
        {
            await page.Locator("[data-testid^='create-draft-']").First.ClickAsync();
        }

        await page.WaitForURLAsync("**/prihlasky/*");
        await WaitForInteractiveReadyAsync(page);

        await page.GetByTestId("attendee-first-name").FillAsync("Blanka");
        await page.GetByTestId("attendee-last-name").FillAsync("Richtarová");
        await page.GetByTestId("attendee-birth-year").FillAsync("1982");
        await page.GetByTestId("type-adult").ClickAsync();

        // Select multiple roles
        await page.Locator("#ar-monster").ClickAsync();
        await page.Locator("#ar-tech").ClickAsync();

        await page.GetByTestId("add-attendee-submit").ClickAsync();

        try
        {
            await page.GetByText("Účastník byl přidán.").WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });
        }
        catch (TimeoutException)
        {
            var bodyText = await page.Locator("body").InnerTextAsync();
            throw new XunitException($"Adult with multiple roles failed. Page body:{Environment.NewLine}{bodyText}");
        }

        await AssertNoBlazorErrorsAsync(page);
        await page.CloseAsync();
    }

    [Fact]
    public async Task RegisterFamilyOfThree_AdultAndTwoChildren()
    {
        var page = await _fixture.Browser.NewPageAsync();
        await LoginAsync(page, RegistrantEmail);

        await page.GotoAsync($"{_fixture.BaseUrl}/moje-prihlasky");
        await WaitForInteractiveReadyAsync(page);

        // Create or resume draft
        var existingDraft = page.Locator("[data-testid^='open-submission-']").First;
        if (await existingDraft.IsVisibleAsync())
        {
            await existingDraft.ClickAsync();
        }
        else
        {
            await page.Locator("[data-testid^='create-draft-']").First.ClickAsync();
        }

        await page.WaitForURLAsync("**/prihlasky/*");
        await WaitForInteractiveReadyAsync(page);

        // --- Add adult parent ---
        await page.GetByTestId("attendee-first-name").FillAsync("Karel");
        await page.GetByTestId("attendee-last-name").FillAsync("Dvořák");
        await page.GetByTestId("attendee-birth-year").FillAsync("1982");
        await page.GetByTestId("type-adult").ClickAsync();
        await page.Locator("#ar-tech").ClickAsync();
        await page.GetByTestId("add-attendee-submit").ClickAsync();

        try
        {
            await page.GetByText("Účastník byl přidán.").WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        }
        catch (TimeoutException)
        {
            var bodyText = await page.Locator("body").InnerTextAsync();
            throw new XunitException($"Adult parent add failed. Page body:{Environment.NewLine}{bodyText}");
        }

        // Reload to get clean form
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForInteractiveReadyAsync(page);

        // --- Add first child (older, independent player) ---
        await page.GetByTestId("attendee-first-name").FillAsync("Eliška");
        await page.GetByTestId("attendee-last-name").FillAsync("Dvořáková");
        await page.GetByTestId("attendee-birth-year").FillAsync("2013");
        await page.GetByTestId("type-player").ClickAsync();
        await page.Locator("#pst-independent").ClickAsync();
        await page.Locator("#guardian-name").FillAsync("Karel Dvořák");
        await page.Locator("#guardian-relationship").FillAsync("otec");
        await page.Locator("#guardian-confirmed").ClickAsync();
        await page.GetByTestId("add-attendee-submit").ClickAsync();

        try
        {
            await page.GetByText("Účastník byl přidán.").WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        }
        catch (TimeoutException)
        {
            var bodyText = await page.Locator("body").InnerTextAsync();
            throw new XunitException($"First child add failed. Page body:{Environment.NewLine}{bodyText}");
        }

        // Reload to get clean form (EditForm in SSR doesn't reset inputs after model change)
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForInteractiveReadyAsync(page);

        // --- Add second child (younger, with ranger) ---
        await page.GetByTestId("attendee-first-name").FillAsync("Matěj");
        await page.GetByTestId("attendee-last-name").FillAsync("Dvořák");
        await page.GetByTestId("attendee-birth-year").FillAsync("2018");
        await page.GetByTestId("type-player").ClickAsync();
        await page.Locator("#pst-ranger").ClickAsync();
        await page.Locator("#guardian-name").FillAsync("Karel Dvořák");
        await page.Locator("#guardian-relationship").FillAsync("otec");
        await page.Locator("#guardian-confirmed").ClickAsync();
        await page.GetByTestId("add-attendee-submit").ClickAsync();

        try
        {
            await page.GetByText("Účastník byl přidán.").WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        }
        catch (TimeoutException)
        {
            var bodyText = await page.Locator("body").InnerTextAsync();
            throw new XunitException($"Second child add failed. Page body:{Environment.NewLine}{bodyText}");
        }

        await WaitForInteractiveReadyAsync(page);

        // Verify all 3 attendees appear
        var attendeeCards = page.Locator("[data-testid^='attendee-card-']");
        var cardCount = await attendeeCards.CountAsync();
        if (cardCount < 3)
        {
            var bodyText = await page.Locator("body").InnerTextAsync();
            throw new XunitException($"Expected at least 3 attendee cards, got {cardCount}. Page body:{Environment.NewLine}{bodyText}");
        }

        // Verify names appear
        var pageText = await page.Locator("body").InnerTextAsync();
        Assert.Contains("Karel Dvořák", pageText);
        Assert.Contains("Eliška Dvořáková", pageText);
        Assert.Contains("Matěj Dvořák", pageText);

        // Verify mix of roles
        Assert.Contains("Dospělý", pageText);
        Assert.Contains("Hráč", pageText);

        await AssertNoBlazorErrorsAsync(page);
        await page.CloseAsync();
    }

    [Fact]
    public async Task MagicLink_RequestAndVerify_LogsUserIn()
    {
        var testEmail = $"magiclink-{Guid.NewGuid():N}@ovcina.test";
        var page = await _fixture.Browser.NewPageAsync();

        // 1. Navigate to login page
        await page.GotoAsync($"{_fixture.BaseUrl}/Account/Login", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });

        await page.GetByTestId("login-email").WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 5000
        });

        // 2. Fill email and submit magic link request
        await page.GetByTestId("login-email").FillAsync(testEmail);
        await page.GetByTestId("login-submit").ClickAsync();

        // 3. Assert redirect to linkSent page with success message
        await page.WaitForURLAsync("**/Account/Login?linkSent=1**", new PageWaitForURLOptions
        {
            Timeout = 5000
        });
        await page.GetByText("Odkaz k přihlášení byl odeslán").WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 5000
        });

        // 4. Retrieve the token from DB (ACS is not configured in test, so email wasn't sent)
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        await using var db = new ApplicationDbContext(options);
        var loginToken = await db.Set<LoginToken>()
            .Where(t => t.Email == testEmail && !t.IsUsed)
            .OrderByDescending(t => t.CreatedAtUtc)
            .FirstOrDefaultAsync();

        Assert.NotNull(loginToken);

        // 5. Verify the magic link — should create user and sign in
        await page.GotoAsync($"{_fixture.BaseUrl}/Account/VerifyMagicLink?token={loginToken.Token}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });

        // 6. Assert user is logged in — should be redirected to home
        await page.WaitForURLAsync($"{_fixture.BaseUrl}/", new PageWaitForURLOptions
        {
            Timeout = 5000
        });

        // Verify logged-in state: logout button or user email visible
        try
        {
            await page.GetByTestId("logout-button").WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });
        }
        catch (TimeoutException)
        {
            var bodyText = await page.Locator("body").InnerTextAsync();
            throw new XunitException(
                $"Magic link login did not result in logged-in state. Page body:{Environment.NewLine}{bodyText}");
        }

        await AssertNoBlazorErrorsAsync(page);
        await page.CloseAsync();
    }

    private sealed record SeededFoodSummaryData(
        int GameId,
        string GameName,
        DateTime DayOne,
        DateTime DayTwo,
        int SoupOptionId,
        int LunchOptionId);
}
