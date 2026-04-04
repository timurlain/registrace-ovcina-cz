using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Components;
using RegistraceOvcina.Web.Components.Account;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Email;
using RegistraceOvcina.Web.Features.Food;
using RegistraceOvcina.Web.Features.Games;
using RegistraceOvcina.Web.Features.Payments;
using RegistraceOvcina.Web.Features.Kingdoms;
using RegistraceOvcina.Web.Features.Submissions;
using RegistraceOvcina.Web.Security;

namespace RegistraceOvcina.Web;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var defaultCulture = new CultureInfo("cs-CZ");

        CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
        CultureInfo.DefaultThreadCurrentUICulture = defaultCulture;

        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
        {
            builder.WebHost.UseStaticWebAssets();
        }

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<IdentityRedirectManager>();
        builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

        var authBuilder = builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            });
        authBuilder.AddIdentityCookies();

        // Microsoft
        var microsoftConfig = builder.Configuration.GetSection("ExternalAuth:Microsoft");
        if (!string.IsNullOrEmpty(microsoftConfig["ClientId"]) && !string.IsNullOrEmpty(microsoftConfig["ClientSecret"]))
        {
            authBuilder.AddMicrosoftAccount(options =>
            {
                options.ClientId = microsoftConfig["ClientId"]!;
                options.ClientSecret = microsoftConfig["ClientSecret"]!;
            });
        }

        // Google
        var googleConfig = builder.Configuration.GetSection("ExternalAuth:Google");
        if (!string.IsNullOrEmpty(googleConfig["ClientId"]) && !string.IsNullOrEmpty(googleConfig["ClientSecret"]))
        {
            authBuilder.AddGoogle(options =>
            {
                options.ClientId = googleConfig["ClientId"]!;
                options.ClientSecret = googleConfig["ClientSecret"]!;
            });
        }

        // Seznam (OpenID Connect)
        var seznamConfig = builder.Configuration.GetSection("ExternalAuth:Seznam");
        if (!string.IsNullOrEmpty(seznamConfig["ClientId"]) && !string.IsNullOrEmpty(seznamConfig["ClientSecret"]))
        {
            authBuilder.AddOpenIdConnect("Seznam", "Seznam", options =>
            {
                options.Authority = "https://login.szn.cz";
                options.ClientId = seznamConfig["ClientId"]!;
                options.ClientSecret = seznamConfig["ClientSecret"]!;
                options.ResponseType = "code";
                options.CallbackPath = "/signin-seznam";
                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("email");
                options.Scope.Add("profile");
                options.SaveTokens = false;
                options.GetClaimsFromUserInfoEndpoint = true;
            });
        }

        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
        });
        builder.Services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture(defaultCulture);
            options.SupportedCultures = [defaultCulture];
            options.SupportedUICultures = [defaultCulture];
        });
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.AdminOnly, policy => policy.RequireRole(RoleNames.Admin))
            .AddPolicy(AuthorizationPolicies.StaffOnly, policy => policy.RequireRole(RoleNames.Admin, RoleNames.Organizer));

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));
        builder.Services.AddDbContextFactory<ApplicationDbContext>(
            options => options.UseNpgsql(connectionString),
            ServiceLifetime.Scoped);
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();
        builder.Services.Configure<MailboxEmailOptions>(
            builder.Configuration.GetSection(MailboxEmailOptions.SectionName));

        var mailboxEmailOptions = builder.Configuration
            .GetSection(MailboxEmailOptions.SectionName)
            .Get<MailboxEmailOptions>() ?? new MailboxEmailOptions();

        if (mailboxEmailOptions.HasPartialConfiguration)
        {
            throw new InvalidOperationException(MailboxEmailOptions.ValidationMessage);
        }

        builder.Services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 6;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.SignIn.RequireConfirmedAccount = false;
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHttpClient(
            MicrosoftGraphMailboxEmailSender.GraphHttpClientName,
            client => client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/"));

        if (mailboxEmailOptions.IsConfigured)
        {
            builder.Services.AddSingleton<IGraphAccessTokenProvider, MicrosoftGraphAccessTokenProvider>();
            builder.Services.AddSingleton<IEmailSender<ApplicationUser>, MicrosoftGraphMailboxEmailSender>();
        }
        else
        {
            builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
        }

        builder.Services.AddSingleton<SpaydPaymentQrService>();
        builder.Services.AddSingleton<SubmissionPricingService>();
        builder.Services.AddScoped<FoodSummaryService>();
        builder.Services.AddScoped<GameService>();
        builder.Services.AddScoped<KingdomService>();
        builder.Services.AddScoped<SubmissionService>();

        var app = builder.Build();

        app.UseForwardedHeaders();
        app.UseRequestLocalization();

        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
        {
            app.UseMigrationsEndPoint();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

        if (!app.Environment.IsEnvironment("Testing"))
        {
            app.UseHttpsRedirection();
        }

        if (app.Environment.IsDevelopment())
        {
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value ?? string.Empty;
                var isAuthDebugPath =
                    path.StartsWith("/Account/", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/not-found", StringComparison.OrdinalIgnoreCase);

                if (!isAuthDebugPath)
                {
                    await next();
                    return;
                }

                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("AuthDebug");

                logger.LogInformation(
                    "[AuthDebug] START {Method} {Path}{QueryString} | Referer: {Referer} | PID: {ProcessId}",
                    context.Request.Method,
                    context.Request.Path,
                    context.Request.QueryString,
                    context.Request.Headers.Referer.ToString(),
                    Environment.ProcessId);

                try
                {
                    await next();
                }
                finally
                {
                    logger.LogInformation(
                        "[AuthDebug] END {Method} {Path}{QueryString} -> {StatusCode}",
                        context.Request.Method,
                        context.Request.Path,
                        context.Request.QueryString,
                        context.Response.StatusCode);
                }
            });
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        await DatabaseInitializer.InitializeAsync(app);

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        if (app.Environment.IsEnvironment("Testing"))
        {
            app.MapGet(
                    "/testing/login",
                    async (string email, string? returnUrl, HttpContext httpContext, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) =>
                    {
                        var user = await userManager.FindByEmailAsync(email);
                        if (user is null)
                        {
                            return Results.NotFound();
                        }

                        await httpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                        await signInManager.SignInAsync(user, isPersistent: false);

                        return Results.LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
                    })
                .AllowAnonymous();
        }
        app.MapPost(
                "/admin/hry/vytvorit",
                async ([FromForm] CreateGameInput input, HttpContext httpContext, UserManager<ApplicationUser> userManager, GameService gameService) =>
                {
                    if (GetValidationError(input) is { } validationError)
                    {
                        return Results.LocalRedirect($"/admin/hry?error={Uri.EscapeDataString(validationError)}");
                    }

                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString("/admin/hry")}");
                    }

                    try
                    {
                        await gameService.CreateGameAsync(input.ToCommand(), user.Id);
                        return Results.LocalRedirect("/admin/hry?created=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/admin/hry?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPost(
                "/admin/kralovstvi/pridat",
                async ([FromForm] string name, [FromForm] string displayName, [FromForm] string? color, HttpContext httpContext, UserManager<ApplicationUser> userManager, KingdomService kingdomService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString("/admin/kralovstvi")}");
                    }

                    try
                    {
                        await kingdomService.CreateKingdomAsync(name, displayName, color, user.Id);
                        return Results.LocalRedirect("/admin/kralovstvi?created=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/admin/kralovstvi?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPost(
                "/admin/kralovstvi/{id:int}/upravit",
                async (int id, [FromForm] string name, [FromForm] string displayName, [FromForm] string? color, HttpContext httpContext, UserManager<ApplicationUser> userManager, KingdomService kingdomService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString("/admin/kralovstvi")}");
                    }

                    try
                    {
                        await kingdomService.UpdateKingdomAsync(id, name, displayName, color, user.Id);
                        return Results.LocalRedirect("/admin/kralovstvi?updated=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/admin/kralovstvi?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPost(
                "/admin/kralovstvi/{id:int}/smazat",
                async (int id, HttpContext httpContext, UserManager<ApplicationUser> userManager, KingdomService kingdomService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString("/admin/kralovstvi")}");
                    }

                    try
                    {
                        await kingdomService.DeleteKingdomAsync(id, user.Id);
                        return Results.LocalRedirect("/admin/kralovstvi?deleted=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/admin/kralovstvi?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPost(
                "/admin/hry/{gameId:int}/kralovstvi/ulozit",
                async (int gameId, HttpContext httpContext, UserManager<ApplicationUser> userManager, KingdomService kingdomService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/admin/hry/{gameId}/kralovstvi")}");
                    }

                    try
                    {
                        var targets = new List<GameKingdomTargetInput>();
                        foreach (var key in httpContext.Request.Form.Keys)
                        {
                            if (!key.StartsWith("active_")) continue;
                            var kingdomIdStr = key["active_".Length..];
                            if (!int.TryParse(kingdomIdStr, out var kingdomId)) continue;

                            var targetCountStr = httpContext.Request.Form[$"target_{kingdomId}"];
                            var targetCount = int.TryParse(targetCountStr, out var tc) ? tc : 0;

                            targets.Add(new GameKingdomTargetInput(kingdomId, targetCount));
                        }

                        await kingdomService.SaveGameKingdomTargetsAsync(gameId, targets, user.Id);
                        return Results.LocalRedirect($"/admin/hry/{gameId}/kralovstvi?saved=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/admin/hry/{gameId}/kralovstvi?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPost(
                "/prihlasky/vytvorit/{gameId:int}",
                async (HttpContext httpContext, int gameId, UserManager<ApplicationUser> userManager, SubmissionService submissionService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString("/moje-prihlasky")}");
                    }

                    try
                    {
                        var submissionId = await submissionService.CreateOrResumeDraftAsync(gameId, user);
                        return Results.LocalRedirect($"/prihlasky/{submissionId}");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/moje-prihlasky?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization();
        app.MapPost(
                "/prihlasky/{submissionId:int}/kontakt",
                async ([FromForm] ContactInput input, HttpContext httpContext, int submissionId, UserManager<ApplicationUser> userManager, SubmissionService submissionService) =>
                {
                    if (GetValidationError(input) is { } validationError)
                    {
                        return Results.LocalRedirect($"/prihlasky/{submissionId}?error={Uri.EscapeDataString(validationError)}");
                    }

                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/prihlasky/{submissionId}")}");
                    }

                    try
                    {
                        await submissionService.UpdateContactAsync(submissionId, user.Id, input);
                        return Results.LocalRedirect($"/prihlasky/{submissionId}?contactSaved=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/prihlasky/{submissionId}?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization();
        app.MapPost(
                "/prihlasky/{submissionId:int}/ucastnici",
                async ([FromForm] AttendeeInput input, HttpContext httpContext, int submissionId, UserManager<ApplicationUser> userManager, SubmissionService submissionService) =>
                {
                    if (GetValidationError(input) is { } validationError)
                    {
                        return Results.LocalRedirect($"/prihlasky/{submissionId}?error={Uri.EscapeDataString(validationError)}");
                    }

                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/prihlasky/{submissionId}")}");
                    }

                    try
                    {
                        await submissionService.AddAttendeeAsync(submissionId, user.Id, input);
                        return Results.LocalRedirect($"/prihlasky/{submissionId}?attendeeAdded=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/prihlasky/{submissionId}?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization();
        app.MapPost(
                "/prihlasky/{submissionId:int}/ucastnici/{registrationId:int}/odebrat",
                async (HttpContext httpContext, int submissionId, int registrationId, UserManager<ApplicationUser> userManager, SubmissionService submissionService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/prihlasky/{submissionId}")}");
                    }

                    try
                    {
                        await submissionService.RemoveAttendeeAsync(submissionId, registrationId, user.Id);
                        return Results.LocalRedirect($"/prihlasky/{submissionId}?attendeeRemoved=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/prihlasky/{submissionId}?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization();
        app.MapPost(
                "/prihlasky/{submissionId:int}/ucastnici/{registrationId:int}/upravit",
                async ([FromForm] AttendeeInput input, HttpContext httpContext, int submissionId, int registrationId, UserManager<ApplicationUser> userManager, SubmissionService submissionService) =>
                {
                    if (GetValidationError(input) is { } validationError)
                    {
                        return Results.LocalRedirect($"/prihlasky/{submissionId}?error={Uri.EscapeDataString(validationError)}");
                    }

                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/prihlasky/{submissionId}")}");
                    }

                    try
                    {
                        await submissionService.UpdateAttendeeAsync(submissionId, registrationId, user.Id, input);
                        return Results.LocalRedirect($"/prihlasky/{submissionId}?attendeeUpdated=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/prihlasky/{submissionId}?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization();
        app.MapPost(
                "/prihlasky/{submissionId:int}/strava",
                async (HttpContext httpContext, int submissionId, UserManager<ApplicationUser> userManager, SubmissionService submissionService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/prihlasky/{submissionId}")}");
                    }

                    try
                    {
                        var orders = new List<FoodOrderInput>();
                        foreach (var key in httpContext.Request.Form.Keys)
                        {
                            if (!key.StartsWith("food_")) continue;
                            var parts = key.Split('_');
                            if (parts.Length != 3) continue;
                            if (!int.TryParse(parts[1], out var regId)) continue;
                            if (!long.TryParse(parts[2], out var dayTicks)) continue;
                            if (!int.TryParse(httpContext.Request.Form[key], out var mealOptionId)) continue;
                            orders.Add(new FoodOrderInput(regId, mealOptionId, new DateTime(dayTicks, DateTimeKind.Utc)));
                        }

                        await submissionService.SaveFoodOrdersAsync(submissionId, user.Id, orders);
                        return Results.LocalRedirect($"/prihlasky/{submissionId}?foodSaved=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/prihlasky/{submissionId}?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization();
        app.MapPost(
                "/prihlasky/{submissionId:int}/odeslat",
                async (HttpContext httpContext, int submissionId, UserManager<ApplicationUser> userManager, SubmissionService submissionService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/prihlasky/{submissionId}")}");
                    }

                    try
                    {
                        await submissionService.SubmitAsync(submissionId, user.Id);
                        return Results.LocalRedirect($"/prihlasky/{submissionId}?submitted=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/prihlasky/{submissionId}?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization();
        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.MapAdditionalIdentityEndpoints();

        await app.RunAsync();
    }

    private static string? GetValidationError(object model)
    {
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            model,
            new ValidationContext(model),
            validationResults,
            validateAllProperties: true);

        return isValid ? null : validationResults[0].ErrorMessage ?? "Formulář obsahuje neplatná data.";
    }
}
