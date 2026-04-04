using Microsoft.Playwright;
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
            page.GetByRole(AriaRole.Link, new() { Name = "Přihlásit se a začít" }).ClickAsync());

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
            page.GetByRole(AriaRole.Link, new() { Name = "Vytvořit nový účet" }).ClickAsync());

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

        await registrantPage.EvaluateAsync(
            """
            () => {
                const setValue = (selector, value) => {
                    const input = document.querySelector(selector);
                    if (!input) {
                        throw new Error(`Missing attendee field: ${selector}`);
                    }

                    input.value = value;
                    input.dispatchEvent(new Event('input', { bubbles: true }));
                    input.dispatchEvent(new Event('change', { bubbles: true }));
                };

                setValue('[data-testid="attendee-first-name"]', 'Anna');
                setValue('[data-testid="attendee-last-name"]', 'Smoková');
                setValue('[data-testid="attendee-birth-year"]', '2014');
                setValue('#attendee-email', '');
                setValue('#attendee-phone', '');
                setValue('#guardian-name', 'Tomáš Smok');
                setValue('#guardian-relationship', 'otec');

                const role = document.querySelector('[data-testid="attendee-role"]');
                if (!role) {
                    throw new Error('Missing attendee role select.');
                }

                role.value = 'Player';
                role.dispatchEvent(new Event('change', { bubbles: true }));

                const guardianConfirmed = document.querySelector('#guardian-confirmed');
                if (!guardianConfirmed) {
                    throw new Error('Missing guardian confirmation checkbox.');
                }

                guardianConfirmed.checked = true;
                guardianConfirmed.dispatchEvent(new Event('input', { bubbles: true }));
                guardianConfirmed.dispatchEvent(new Event('change', { bubbles: true }));

                const submitButton = document.querySelector('[data-testid="add-attendee-submit"]');
                if (!submitButton || !submitButton.form) {
                    throw new Error('Missing attendee submit button.');
                }

                submitButton.form.requestSubmit();
            }
            """);

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
        Assert.Equal("1200,00 Kč", totalText?.Trim());
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

    private static async Task FillAndCommitAsync(ILocator locator, string value)
    {
        await locator.FillAsync(value);
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
}
