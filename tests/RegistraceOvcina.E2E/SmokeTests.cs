using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using ClosedXML.Excel;
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
    public async Task PublicAccountLinks_OpenLoginAndRegistrationPages()
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

        await page.GotoAsync(_fixture.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });
        await WaitForInteractiveReadyAsync(page);

        await Task.WhenAll(
            page.WaitForURLAsync("**/Account/Register", new PageWaitForURLOptions
            {
                Timeout = 5000
            }),
            page.GetByTestId("home-register-button").ClickAsync());

        await page.GetByTestId("register-email").WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 5000
        });

        await page.CloseAsync();
    }

    [Fact]
    public async Task Registration_AllowsSimplePasswordWithoutSpecialCharacter()
    {
        var page = await _fixture.Browser.NewPageAsync();
        var email = $"registrace-{Guid.NewGuid():N}@example.cz";
        const string password = "ovcina";

        await page.GotoAsync($"{_fixture.BaseUrl}/Account/Register", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });

        await page.GetByTestId("register-display-name").FillAsync("Test Rodina");
        await page.GetByTestId("register-email").FillAsync(email);
        await page.GetByTestId("register-password").FillAsync(password);
        await page.GetByTestId("register-confirm-password").FillAsync(password);

        await Task.WhenAll(
            page.WaitForURLAsync($"{_fixture.BaseUrl}/", new PageWaitForURLOptions
            {
                Timeout = 5000
            }),
            page.GetByTestId("register-submit").ClickAsync());

        await WaitForInteractiveReadyAsync(page);
        try
        {
            await page.GetByText(email).WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });
            await page.GetByTestId("logout-button").WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });
        }
        catch (TimeoutException)
        {
            var bodyText = await page.Locator("body").InnerTextAsync();
            throw new XunitException(
                $"Registration did not complete. Url: {page.Url}{Environment.NewLine}"
                + $"Page body:{Environment.NewLine}{bodyText}{Environment.NewLine}"
                + $"Host diagnostics:{Environment.NewLine}{_fixture.GetDiagnostics()}");
        }

        await AssertNoBlazorErrorsAsync(page);
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
    public async Task AdminCanUploadHistoricalImportWorkbook()
    {
        var adminPage = await _fixture.Browser.NewPageAsync();
        var workbookPath = CreateHistoricalImportWorkbookFile();

        try
        {
            await LoginAsync(adminPage, AdminEmail);
            await adminPage.GotoAsync($"{_fixture.BaseUrl}/admin/importy", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });
            await WaitForInteractiveReadyAsync(adminPage);

            await adminPage.GetByTestId("historical-import-title").WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });

            await FillAndCommitAsync(adminPage.GetByTestId("historical-import-label"), $"E2E historie {Guid.NewGuid():N}"[..18]);
            await FillAndCommitAsync(adminPage.GetByTestId("historical-import-game-name"), $"Historie E2E {DateTime.UtcNow:HHmmss}");
            await FillAndCommitAsync(adminPage.GetByTestId("historical-import-starts"), "2025-05-03T08:00");
            await FillAndCommitAsync(adminPage.GetByTestId("historical-import-ends"), "2025-05-04T16:00");
            await adminPage.GetByTestId("historical-import-file").SetInputFilesAsync(workbookPath);

            await Task.WhenAll(
                adminPage.WaitForURLAsync("**/admin/importy?status=imported&batchId=*", new PageWaitForURLOptions
                {
                    Timeout = 10000
                }),
                adminPage.GetByTestId("historical-import-submit").ClickAsync());

            await WaitForInteractiveReadyAsync(adminPage);
            await adminPage.GetByText("Historický import byl dokončen.").WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });

            Assert.Equal("2", (await adminPage.GetByTestId("historical-import-total-rows").TextContentAsync())?.Trim());
            Assert.Equal("0", (await adminPage.GetByTestId("historical-import-warning-count").TextContentAsync())?.Trim());

            await AssertNoBlazorErrorsAsync(adminPage);
        }
        finally
        {
            await adminPage.CloseAsync();
            if (File.Exists(workbookPath))
            {
                File.Delete(workbookPath);
            }
        }
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
        await FillAndCommitAsync(registrantPage.GetByTestId("contact-name"), "Rodina Smokova");
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
        await registrantPage.Locator("#pst-independent").CheckAsync();
        await registrantPage.Locator("#guardian-name").FillAsync("Tomáš Smok");
        await registrantPage.Locator("#guardian-relationship").FillAsync("otec");
        await registrantPage.Locator("#guardian-confirmed").CheckAsync();
        await registrantPage.GetByTestId("add-attendee-submit").ClickAsync();

        try
        {
            await registrantPage.Locator("[data-testid^='attendee-card-']").First.WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });
            await registrantPage.GetByText("Účastník byl přidaný.").WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });
        }
        catch (TimeoutException)
        {
            var bodyText = await registrantPage.Locator("body").InnerTextAsync();
            throw new XunitException($"Attendee add did not complete. Page body:{Environment.NewLine}{bodyText}");
        }

        await WaitForInteractiveReadyAsync(registrantPage);

        await registrantPage.GetByTestId("submit-submission").ClickAsync();

        try
        {
            await registrantPage.GetByText("Přihláška byla odeslaná.").WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });
            await registrantPage.GetByTestId("payment-qr").WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 5000
            });
        }
        catch (TimeoutException)
        {
            var bodyText = await registrantPage.Locator("body").InnerTextAsync();
            throw new XunitException($"Submission did not complete. Page body:{Environment.NewLine}{bodyText}");
        }

        await registrantPage.GetByTestId("submission-total").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });

        var totalText = await registrantPage.GetByTestId("submission-total").TextContentAsync();
        Assert.Equal("100,00 Kč", totalText?.Trim());
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
    }

    private static string CreateHistoricalImportWorkbookFile()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Registrační formulář");
        var headers = new[]
        {
            "Časová značka",
            "Název skupiny",
            "Jméno a Příjmení",
            "Jméno hrané postavy.",
            "Věk účastníka",
            "Typ účastníka",
            "Osobní quest",
            "Představa osobního questu",
            "Jídlo sobota",
            "Jídlo neděle",
            "Zákonný zástupce",
            "Poznámka",
            "Kontaktní email",
            "Kontaktní telefon",
            "Hráč",
            "NPC",
            "Příšera",
            "Technická pomoc",
            "Národ",
            "Osobní quest vytvořený",
            "Link na quest"
        };

        for (var index = 0; index < headers.Length; index++)
        {
            sheet.Cell(1, index + 1).Value = headers[index];
        }

        sheet.Cell(2, 1).Value = "2025-03-31 11:17:46";
        sheet.Cell(2, 2).Value = "Kopečci";
        sheet.Cell(2, 3).Value = "Tomáš Kopecký";
        sheet.Cell(2, 4).Value = "Tomas";
        sheet.Cell(2, 5).Value = "15";
        sheet.Cell(2, 6).Value = "hrající samostatné dítě (cca 10+ , které chce naplno soutěžit s dalšími - PVP hráč)";
        sheet.Cell(2, 11).Value = "Magda Kopecká";
        sheet.Cell(2, 12).Value = "Nový Arnor";
        sheet.Cell(2, 13).Value = "magda.kopecka@email.cz";
        sheet.Cell(2, 14).Value = "603356214";
        sheet.Cell(2, 15).Value = true;
        sheet.Cell(2, 19).Value = "Nový Arnor";

        sheet.Cell(3, 1).Value = "2025-03-31 11:02:44";
        sheet.Cell(3, 2).Value = "Kopečci";
        sheet.Cell(3, 3).Value = "Magda Kopecká";
        sheet.Cell(3, 5).Value = "45";
        sheet.Cell(3, 6).Value = "dospělý se zájmem hrát cizí postavu - příšeru (např. skřet, kostlivec, vlci.)";
        sheet.Cell(3, 13).Value = "magda.kopecka@email.cz";
        sheet.Cell(3, 14).Value = "603356214";
        sheet.Cell(3, 17).Value = true;
        sheet.Cell(3, 19).Value = "Příšera";

        var path = Path.Combine(Path.GetTempPath(), $"historical-import-{Guid.NewGuid():N}.xlsx");
        workbook.SaveAs(path);
        return path;
    }

    private static async Task WaitForInteractiveReadyAsync(IPage page)
    {
        await page.GetByTestId("interactive-ready").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 5000
        });
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

    private sealed record SeededFoodSummaryData(
        int GameId,
        string GameName,
        DateTime DayOne,
        DateTime DayTwo,
        int SoupOptionId,
        int LunchOptionId);
}
