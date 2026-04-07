using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using RegistraceOvcina.Web.Components.Account.Pages;
using RegistraceOvcina.Web.Components.Account.Pages.Manage;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Auth;
using RegistraceOvcina.Web.Security;

namespace Microsoft.AspNetCore.Routing;

internal static class IdentityComponentsEndpointRouteBuilderExtensions
{
    // These endpoints are required by the Identity Razor components defined in the /Components/Account/Pages directory of this project.
    public static IEndpointConventionBuilder MapAdditionalIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var accountGroup = endpoints.MapGroup("/Account");

        accountGroup.MapPost("/PerformExternalLogin", (
            HttpContext context,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromForm] string provider,
            [FromForm] string returnUrl) =>
        {
            IEnumerable<KeyValuePair<string, StringValues>> query = [
                new("ReturnUrl", returnUrl),
                new("Action", ExternalLogin.LoginCallbackAction)];

            var redirectUrl = UriHelper.BuildRelative(
                context.Request.PathBase,
                "/Account/ExternalLogin",
                QueryString.Create(query));

            var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return TypedResults.Challenge(properties, [provider]);
        });

        accountGroup.MapPost("/Logout", async (
            ClaimsPrincipal user,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromForm] string returnUrl) =>
        {
            await signInManager.SignOutAsync();
            return TypedResults.LocalRedirect($"~/{returnUrl}");
        });

        accountGroup.MapPost("/RequestMagicLink", async (
            [FromForm] string email,
            [FromForm] string? returnUrl,
            [FromServices] MagicLinkAuthService magicLinkService,
            [FromServices] AcsTransactionalEmailService emailService,
            HttpContext context) =>
        {
            var trimmedEmail = email.Trim();
            var isValidEmailInput = !string.IsNullOrWhiteSpace(trimmedEmail) && trimmedEmail.Length <= 256;

            if (isValidEmailInput)
            {
                var loginToken = await magicLinkService.RequestMagicLinkAsync(trimmedEmail);

                if (loginToken is not null)
                {
                    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
                    var verifyUrl = $"{baseUrl}/Account/VerifyMagicLink?token={loginToken.Token}";
                    if (!string.IsNullOrWhiteSpace(returnUrl))
                    {
                        verifyUrl += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
                    }

                    try
                    {
                        await emailService.SendMagicLinkAsync(loginToken.Email, verifyUrl);
                    }
                    catch (Exception ex)
                    {
                        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                            .CreateLogger("MagicLink");
                        logger.LogError(ex, "Failed to send magic link email to {Email}", loginToken.Email);
                    }
                }
            }

            // Always redirect to success (don't reveal whether email exists)
            var redirectUrl = $"/Account/Login?linkSent=1";
            if (!string.IsNullOrWhiteSpace(returnUrl))
            {
                redirectUrl += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
            }
            return Results.Redirect(redirectUrl);
        }).AllowAnonymous();

        accountGroup.MapGet("/VerifyMagicLink", async (
            [FromQuery] string token,
            [FromQuery] string? returnUrl,
            [FromServices] MagicLinkAuthService magicLinkService,
            [FromServices] UserManager<ApplicationUser> userManager,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromServices] TimeProvider timeProvider) =>
        {
            var loginToken = await magicLinkService.VerifyTokenAsync(token);
            if (loginToken is null)
            {
                return Results.Redirect("/Account/Login?error=invalid-token");
            }

            // Find or create user
            var user = loginToken.UserId is not null
                ? await userManager.FindByIdAsync(loginToken.UserId)
                : await userManager.FindByEmailAsync(loginToken.Email);

            if (user is null)
            {
                // Auto-create account on first magic link verification
                user = new ApplicationUser
                {
                    UserName = loginToken.Email,
                    Email = loginToken.Email,
                    EmailConfirmed = true,
                    IsActive = true,
                    CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
                };

                var createResult = await userManager.CreateAsync(user);
                if (!createResult.Succeeded)
                {
                    return Results.Redirect("/Account/Login?error=create-failed");
                }

                await userManager.AddToRoleAsync(user, RoleNames.Registrant);
            }

            if (!user.IsActive)
            {
                return Results.Redirect("/Account/Login?error=inactive");
            }

            user.LastLoginAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            await userManager.UpdateAsync(user);

            await signInManager.SignInAsync(user, isPersistent: true);

            return Results.LocalRedirect(returnUrl ?? "~/");
        }).AllowAnonymous();

        accountGroup.MapPost("/PasskeyCreationOptions", async (
            HttpContext context,
            [FromServices] UserManager<ApplicationUser> userManager,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromServices] IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context);

            var user = await userManager.GetUserAsync(context.User);
            if (user is null)
            {
                return Results.NotFound($"Unable to load user with ID '{userManager.GetUserId(context.User)}'.");
            }

            var userId = await userManager.GetUserIdAsync(user);
            var userName = await userManager.GetUserNameAsync(user) ?? "User";
            var optionsJson = await signInManager.MakePasskeyCreationOptionsAsync(new()
            {
                Id = userId,
                Name = userName,
                DisplayName = userName
            });
            return TypedResults.Content(optionsJson, contentType: "application/json");
        });

        accountGroup.MapPost("/PasskeyRequestOptions", async (
            HttpContext context,
            [FromServices] UserManager<ApplicationUser> userManager,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromServices] IAntiforgery antiforgery,
            [FromQuery] string? username) =>
        {
            await antiforgery.ValidateRequestAsync(context);

            var user = string.IsNullOrEmpty(username) ? null : await userManager.FindByNameAsync(username);
            var optionsJson = await signInManager.MakePasskeyRequestOptionsAsync(user);
            return TypedResults.Content(optionsJson, contentType: "application/json");
        });

        var manageGroup = accountGroup.MapGroup("/Manage").RequireAuthorization();

        manageGroup.MapPost("/LinkExternalLogin", async (
            HttpContext context,
            [FromServices] SignInManager<ApplicationUser> signInManager,
            [FromForm] string provider) =>
        {
            // Clear the existing external cookie to ensure a clean login process
            await context.SignOutAsync(IdentityConstants.ExternalScheme);

            var redirectUrl = UriHelper.BuildRelative(
                context.Request.PathBase,
                "/Account/Manage/ExternalLogins",
                QueryString.Create("Action", ExternalLogins.LinkLoginCallbackAction));

            var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl, signInManager.UserManager.GetUserId(context.User));
            return TypedResults.Challenge(properties, [provider]);
        });

        var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var downloadLogger = loggerFactory.CreateLogger("DownloadPersonalData");

        manageGroup.MapPost("/DownloadPersonalData", async (
            HttpContext context,
            [FromServices] UserManager<ApplicationUser> userManager,
            [FromServices] AuthenticationStateProvider authenticationStateProvider) =>
        {
            var user = await userManager.GetUserAsync(context.User);
            if (user is null)
            {
                return Results.NotFound($"Unable to load user with ID '{userManager.GetUserId(context.User)}'.");
            }

            var userId = await userManager.GetUserIdAsync(user);
            downloadLogger.LogInformation("User with ID '{UserId}' asked for their personal data.", userId);

            // Only include personal data for download
            var personalData = new Dictionary<string, string>();
            var personalDataProps = typeof(ApplicationUser).GetProperties().Where(
                prop => Attribute.IsDefined(prop, typeof(PersonalDataAttribute)));
            foreach (var p in personalDataProps)
            {
                personalData.Add(p.Name, p.GetValue(user)?.ToString() ?? "null");
            }

            var logins = await userManager.GetLoginsAsync(user);
            foreach (var l in logins)
            {
                personalData.Add($"{l.LoginProvider} external login provider key", l.ProviderKey);
            }

            personalData.Add("Authenticator Key", (await userManager.GetAuthenticatorKeyAsync(user))!);
            var fileBytes = JsonSerializer.SerializeToUtf8Bytes(personalData);

            context.Response.Headers.TryAdd("Content-Disposition", "attachment; filename=PersonalData.json");
            return TypedResults.File(fileBytes, contentType: "application/json", fileDownloadName: "PersonalData.json");
        });

        return accountGroup;
    }
}
