using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RegistraceOvcina.Web.Components;
using RegistraceOvcina.Web.Components.Account;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Email;
using RegistraceOvcina.Web.Features.Food;
using RegistraceOvcina.Web.Features.Games;
using RegistraceOvcina.Web.Features.Invitations;
using RegistraceOvcina.Web.Features.HistoricalImport;
using RegistraceOvcina.Web.Features.Payments;
using RegistraceOvcina.Web.Features.Kingdoms;
using RegistraceOvcina.Web.Features.People;
using RegistraceOvcina.Web.Features.Submissions;
using RegistraceOvcina.Web.Features.Integration;
using RegistraceOvcina.Web.Features.Roles;
using RegistraceOvcina.Web.Features.Auth;
using RegistraceOvcina.Web.Features.Users;
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

        // Seznam (OAuth2 — not standard OIDC, uses custom endpoints)
        var seznamConfig = builder.Configuration.GetSection("ExternalAuth:Seznam");
        if (!string.IsNullOrEmpty(seznamConfig["ClientId"]) && !string.IsNullOrEmpty(seznamConfig["ClientSecret"]))
        {
            authBuilder.AddOAuth("Seznam", "Seznam", options =>
            {
                options.ClientId = seznamConfig["ClientId"]!;
                options.ClientSecret = seznamConfig["ClientSecret"]!;
                options.AuthorizationEndpoint = "https://login.szn.cz/api/v1/oauth/auth";
                options.TokenEndpoint = "https://login.szn.cz/api/v1/oauth/token";
                options.UserInformationEndpoint = "https://login.szn.cz/api/v1/user";
                options.CallbackPath = "/signin-seznam";
                options.Scope.Add("identity");
                options.SaveTokens = false;
                options.ClaimActions.MapJsonKey(System.Security.Claims.ClaimTypes.NameIdentifier, "oauth_user_id");
                options.ClaimActions.MapJsonKey(System.Security.Claims.ClaimTypes.Email, "email");
                options.ClaimActions.MapJsonKey(System.Security.Claims.ClaimTypes.Name, "firstname");
                options.Events.OnCreatingTicket = async context =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", context.AccessToken);
                    using var response = await context.Backchannel.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    var user = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                    context.RunClaimActions(user);
                };
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

        // EF Core 10 false-positive: CLI reports no pending changes but runtime disagrees.
        // Suppress globally until EF Core fixes this. CI migration checks catch real drift.
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString)
                   .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        builder.Services.AddDbContextFactory<ApplicationDbContext>(
            options => options.UseNpgsql(connectionString)
                              .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)),
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
        builder.Services.AddScoped<MealOptionService>();
        builder.Services.AddScoped<GameService>();
        builder.Services.AddScoped<HistoricalImportService>();
        builder.Services.AddScoped<InboxService>();
        builder.Services.AddScoped<KingdomService>();
        builder.Services.AddScoped<KingdomAssignmentService>();
        builder.Services.AddScoped<PeopleReviewService>();
        builder.Services.AddScoped<SubmissionService>();
        builder.Services.AddScoped<OrganizerSubmissionService>();
        builder.Services.AddScoped<PaymentService>();
        builder.Services.Configure<AcsEmailOptions>(builder.Configuration.GetSection(AcsEmailOptions.SectionName));
        builder.Services.AddScoped<AcsTransactionalEmailService>();
        builder.Services.AddScoped<UserAdministrationService>();
        builder.Services.AddScoped<GameRoleService>();
        builder.Services.Configure<IntegrationApiOptions>(
            builder.Configuration.GetSection(IntegrationApiOptions.SectionName));

        if (mailboxEmailOptions.IsConfigured)
        {
            builder.Services.AddScoped<MailboxSyncService>();
            builder.Services.AddScoped<InvitationService>();
        }

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

        // Redirect to kingdom assignment for the latest published game
        app.MapGet("/organizace/rozdeleni", async (IDbContextFactory<ApplicationDbContext> dbFactory) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var latestGameId = await db.Games
                .Where(x => x.IsPublished)
                .OrderByDescending(x => x.StartsAtUtc)
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync();

            return latestGameId.HasValue
                ? Results.LocalRedirect($"/organizace/hry/{latestGameId.Value}/kralovstvi")
                : Results.LocalRedirect("/organizace/prihlasky");
        }).RequireAuthorization(AuthorizationPolicies.StaffOnly);
        app.MapIntegrationApi();
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
                "/admin/hry/{gameId:int}/upravit",
                async (int gameId, [FromForm] CreateGameInput input, HttpContext httpContext, UserManager<ApplicationUser> userManager, GameService gameService) =>
                {
                    if (GetValidationError(input) is { } validationError)
                    {
                        return Results.LocalRedirect($"/admin/hry?edit={gameId}&error={Uri.EscapeDataString(validationError)}");
                    }

                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString("/admin/hry")}");
                    }

                    try
                    {
                        await gameService.UpdateGameAsync(gameId, input.ToCommand(), user.Id);
                        return Results.LocalRedirect("/admin/hry?updated=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/admin/hry?edit={gameId}&error={Uri.EscapeDataString(ex.Message)}");
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
                "/admin/organizatori/{userId}/organizator",
                async (string userId, HttpContext httpContext, UserManager<ApplicationUser> userManager, UserAdministrationService userAdministrationService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString("/admin/organizatori")}");
                    }

                    try
                    {
                        var result = await userAdministrationService.ToggleRoleAsync(userId, RoleNames.Organizer, user.Id);
                        return Results.LocalRedirect($"/admin/organizatori?status={Uri.EscapeDataString(result.StatusCode)}");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/admin/organizatori?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPost(
                "/admin/organizatori/{userId}/spravce",
                async (string userId, HttpContext httpContext, UserManager<ApplicationUser> userManager, UserAdministrationService userAdministrationService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString("/admin/organizatori")}");
                    }

                    try
                    {
                        var result = await userAdministrationService.ToggleRoleAsync(userId, RoleNames.Admin, user.Id);
                        return Results.LocalRedirect($"/admin/organizatori?status={Uri.EscapeDataString(result.StatusCode)}");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/admin/organizatori?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPost(
                "/admin/organizatori/{userId}/aktivita",
                async (string userId, HttpContext httpContext, UserManager<ApplicationUser> userManager, UserAdministrationService userAdministrationService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString("/admin/organizatori")}");
                    }

                    try
                    {
                        var result = await userAdministrationService.ToggleActiveAsync(userId, user.Id);
                        return Results.LocalRedirect($"/admin/organizatori?status={Uri.EscapeDataString(result.StatusCode)}");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/admin/organizatori?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPost(
                "/admin/importy/spustit",
                async ([FromForm] HistoricalImportInput input, IFormFile? workbook, HttpContext httpContext, UserManager<ApplicationUser> userManager, HistoricalImportService historicalImportService) =>
                {
                    if (GetValidationError(input) is { } validationError)
                    {
                        return Results.LocalRedirect($"/admin/importy?error={Uri.EscapeDataString(validationError)}");
                    }

                    if (workbook is null || workbook.Length == 0)
                    {
                        return Results.LocalRedirect("/admin/importy?error=Vyberte%20Excel%20soubor%20pro%20import.");
                    }

                    if (!workbook.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                    {
                        return Results.LocalRedirect("/admin/importy?error=Import%20aktuálně%20podporuje%20jen%20soubory%20.xlsx.");
                    }

                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString("/admin/importy")}");
                    }

                    try
                    {
                        await using var stream = workbook.OpenReadStream();
                        var result = await historicalImportService.ImportWorkbookAsync(input.ToCommand(workbook.FileName), stream, user.Id);
                        return Results.LocalRedirect($"/admin/importy?status=imported&batchId={result.BatchId}");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/admin/importy?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPost(
                "/admin/hry/{gameId:int}/jidla/pridat",
                async (int gameId, [FromForm] string name, [FromForm] decimal price, HttpContext httpContext, UserManager<ApplicationUser> userManager, MealOptionService mealOptionService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/admin/hry/{gameId}/jidla")}");
                    }

                    try
                    {
                        await mealOptionService.CreateMealOptionAsync(gameId, name, price, user.Id);
                        return Results.LocalRedirect($"/admin/hry/{gameId}/jidla?created=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/admin/hry/{gameId}/jidla?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPost(
                "/admin/hry/{gameId:int}/jidla/{id:int}/upravit",
                async (int gameId, int id, [FromForm] string name, [FromForm] decimal price, HttpContext httpContext, UserManager<ApplicationUser> userManager, MealOptionService mealOptionService) =>
                {
                    var isActive = httpContext.Request.Form.ContainsKey("isActive");
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/admin/hry/{gameId}/jidla")}");
                    }

                    try
                    {
                        await mealOptionService.UpdateMealOptionAsync(id, name, price, isActive, user.Id);
                        return Results.LocalRedirect($"/admin/hry/{gameId}/jidla?updated=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/admin/hry/{gameId}/jidla?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPost(
                "/admin/hry/{gameId:int}/jidla/{id:int}/smazat",
                async (int gameId, int id, HttpContext httpContext, UserManager<ApplicationUser> userManager, MealOptionService mealOptionService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/admin/hry/{gameId}/jidla")}");
                    }

                    try
                    {
                        await mealOptionService.DeleteMealOptionAsync(id, user.Id);
                        return Results.LocalRedirect($"/admin/hry/{gameId}/jidla?deleted=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/admin/hry/{gameId}/jidla?error={Uri.EscapeDataString(ex.Message)}");
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
                "/prihlasky/zopakovat/{sourceSubmissionId:int}/{targetGameId:int}",
                async (HttpContext httpContext, int sourceSubmissionId, int targetGameId, UserManager<ApplicationUser> userManager, SubmissionService submissionService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString("/moje-prihlasky")}");
                    }

                    try
                    {
                        var submissionId = await submissionService.RepeatSubmissionAsync(sourceSubmissionId, targetGameId, user);
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
        app.MapPost(
                "/prihlasky/{submissionId:int}/smazat",
                async (HttpContext httpContext, int submissionId, UserManager<ApplicationUser> userManager, SubmissionService submissionService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString("/moje-prihlasky")}");
                    }

                    try
                    {
                        await submissionService.DeleteSubmissionAsync(submissionId, user.Id, isStaff: false);
                        return Results.LocalRedirect("/moje-prihlasky?deleted=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/prihlasky/{submissionId}?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization();
        app.MapPost(
                "/organizace/prihlasky/{submissionId:int}/smazat",
                async (HttpContext httpContext, int submissionId, UserManager<ApplicationUser> userManager, SubmissionService submissionService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString("/organizace/prihlasky")}");
                    }

                    try
                    {
                        await submissionService.DeleteSubmissionAsync(submissionId, user.Id, isStaff: true);
                        return Results.LocalRedirect("/organizace/prihlasky?deleted=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/organizace/prihlasky?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.StaffOnly);
        app.MapPost(
                "/organizace/posta/sync",
                async (HttpContext httpContext, MailboxSyncService? syncService) =>
                {
                    if (syncService is null)
                    {
                        return Results.LocalRedirect("/organizace/posta");
                    }

                    await syncService.SyncInboxAsync(httpContext.RequestAborted);
                    return Results.LocalRedirect("/organizace/posta?synced=1");
                })
            .RequireAuthorization(AuthorizationPolicies.StaffOnly);
        app.MapPost(
                "/organizace/posta/{messageId:int}/propojit-prihlasku",
                async ([FromForm] int submissionId, int messageId, HttpContext httpContext, UserManager<ApplicationUser> userManager, InboxService inboxService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/organizace/posta/{messageId}")}");
                    }

                    await inboxService.LinkToSubmissionAsync(messageId, submissionId, user.Id);
                    return Results.LocalRedirect($"/organizace/posta/{messageId}?linked=1");
                })
            .RequireAuthorization(AuthorizationPolicies.StaffOnly);
        app.MapPost(
                "/organizace/posta/{messageId:int}/propojit-osobu",
                async ([FromForm] int personId, int messageId, HttpContext httpContext, UserManager<ApplicationUser> userManager, InboxService inboxService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/organizace/posta/{messageId}")}");
                    }

                    await inboxService.LinkToPersonAsync(messageId, personId, user.Id);
                    return Results.LocalRedirect($"/organizace/posta/{messageId}?linked=1");
                })
            .RequireAuthorization(AuthorizationPolicies.StaffOnly);
        app.MapPost(
                "/organizace/posta/{messageId:int}/odpojit",
                async (int messageId, HttpContext httpContext, UserManager<ApplicationUser> userManager, InboxService inboxService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/organizace/posta/{messageId}")}");
                    }

                    await inboxService.UnlinkAsync(messageId, user.Id);
                    return Results.LocalRedirect($"/organizace/posta/{messageId}?unlinked=1");
                })
            .RequireAuthorization(AuthorizationPolicies.StaffOnly);
        app.MapPost(
                "/organizace/posta/{messageId:int}/odpovedet",
                async ([FromForm] string replyBody, int messageId, HttpContext httpContext, UserManager<ApplicationUser> userManager, InboxService inboxService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/organizace/posta/{messageId}")}");
                    }

                    if (string.IsNullOrWhiteSpace(replyBody))
                    {
                        return Results.LocalRedirect($"/organizace/posta/{messageId}");
                    }

                    try
                    {
                        await inboxService.SendReplyAsync(messageId, replyBody.Trim(), user.Id, httpContext.RequestAborted);
                        return Results.LocalRedirect($"/organizace/posta/{messageId}?replied=1");
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
                    {
                        return Results.LocalRedirect($"/organizace/posta/{messageId}?replyError=1");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.StaffOnly);
        app.MapPost(
                "/organizace/posta/odeslat",
                async ([FromForm] string toEmail, [FromForm] string subject, [FromForm] string body, HttpContext httpContext, UserManager<ApplicationUser> userManager, InboxService inboxService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString("/organizace/posta")}");
                    }

                    if (string.IsNullOrWhiteSpace(toEmail) || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
                    {
                        return Results.LocalRedirect("/organizace/posta");
                    }

                    try
                    {
                        await inboxService.SendNewMessageAsync(toEmail.Trim(), subject.Trim(), body.Trim(), null, user.Id, httpContext.RequestAborted);
                        return Results.LocalRedirect("/organizace/posta?sent=1");
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
                    {
                        return Results.LocalRedirect("/organizace/posta?sendError=1");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.StaffOnly);
        app.MapPost(
                "/organizace/posta/hromadne",
                async ([FromForm] string subject, [FromForm] string body, [FromForm] int? gameId, HttpContext httpContext, UserManager<ApplicationUser> userManager, InboxService inboxService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString("/organizace/posta")}");
                    }

                    if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
                    {
                        return Results.LocalRedirect("/organizace/posta?sendError=1");
                    }

                    var recipients = await inboxService.GetBulkRecipientsAsync(gameId, httpContext.RequestAborted);
                    if (recipients.Count == 0)
                    {
                        return Results.LocalRedirect("/organizace/posta?sendError=1");
                    }

                    try
                    {
                        var (sent, failed) = await inboxService.SendBulkEmailAsync(
                            recipients, subject.Trim(), body.Trim(), user.Id, httpContext.RequestAborted);
                        return Results.LocalRedirect($"/organizace/posta?bulkSent={sent}&bulkFailed={failed}");
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
                    {
                        return Results.LocalRedirect("/organizace/posta?sendError=1");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.StaffOnly);
        app.MapPost(
                "/organizace/osoby/{personId:int}/propojit-ucet",
                async ([FromForm] string userId, int personId, HttpContext httpContext, UserManager<ApplicationUser> userManager, PeopleReviewService peopleReviewService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/organizace/osoby/{personId}")}");
                    }

                    try
                    {
                        await peopleReviewService.LinkUserAsync(personId, userId, user.Id);
                        return Results.LocalRedirect($"/organizace/osoby/{personId}?linkedUser=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/organizace/osoby/{personId}?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.StaffOnly);
        app.MapPost(
                "/organizace/osoby/{personId:int}/odpojit-ucet",
                async ([FromForm] string userId, int personId, HttpContext httpContext, UserManager<ApplicationUser> userManager, PeopleReviewService peopleReviewService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/organizace/osoby/{personId}")}");
                    }

                    try
                    {
                        await peopleReviewService.UnlinkUserAsync(personId, userId, user.Id);
                        return Results.LocalRedirect($"/organizace/osoby/{personId}?unlinkedUser=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/organizace/osoby/{personId}?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.StaffOnly);
        app.MapPost(
                "/organizace/osoby/{personId:int}/sloucit",
                async ([FromForm] int duplicatePersonId, int personId, HttpContext httpContext, UserManager<ApplicationUser> userManager, PeopleReviewService peopleReviewService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/organizace/osoby/{personId}")}");
                    }

                    try
                    {
                        await peopleReviewService.MergeAsync(personId, duplicatePersonId, user.Id);
                        return Results.LocalRedirect($"/organizace/osoby/{personId}?merged=1");
                    }
                    catch (ValidationException ex)
                    {
                        return Results.LocalRedirect($"/organizace/osoby/{personId}?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.StaffOnly);
        app.MapPost(
                "/organizace/prihlasky/{submissionId:int}/poznamka",
                async ([FromForm] string note, HttpContext httpContext, int submissionId, UserManager<ApplicationUser> userManager, OrganizerSubmissionService organizerSubmissionService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/organizace/prihlasky/{submissionId}")}");
                    }

                    if (string.IsNullOrWhiteSpace(note))
                    {
                        return Results.LocalRedirect($"/organizace/prihlasky/{submissionId}?error={Uri.EscapeDataString("Poznámka nemůže být prázdná.")}");
                    }

                    try
                    {
                        await organizerSubmissionService.AddNoteAsync(submissionId, note, user.Id);
                        return Results.LocalRedirect($"/organizace/prihlasky/{submissionId}?noteSaved=1");
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.LocalRedirect($"/organizace/prihlasky/{submissionId}?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.StaffOnly);
        app.MapPost(
                "/organizace/platby/{submissionId:int}/zaznamenat",
                async ([FromForm] decimal amount, [FromForm] int method, [FromForm] string? reference, [FromForm] string? note, int submissionId, HttpContext httpContext, UserManager<ApplicationUser> userManager, PaymentService paymentService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString("/organizace/platby")}");
                    }

                    if (amount <= 0)
                    {
                        return Results.LocalRedirect($"/organizace/platby?error={Uri.EscapeDataString("Částka musí být kladná.")}");
                    }

                    if (!Enum.IsDefined(typeof(PaymentMethod), method))
                    {
                        return Results.LocalRedirect($"/organizace/platby?error={Uri.EscapeDataString("Neplatný způsob platby.")}");
                    }

                    try
                    {
                        await paymentService.RecordPaymentAsync(
                            submissionId,
                            amount,
                            (PaymentMethod)method,
                            reference,
                            note,
                            user.Id);
                        return Results.LocalRedirect("/organizace/platby?recorded=1");
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.LocalRedirect($"/organizace/platby?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.StaffOnly);
        app.MapPost(
                "/organizace/hry/{gameId:int}/kralovstvi/pridelit",
                async (int gameId, HttpContext httpContext, UserManager<ApplicationUser> userManager, KingdomAssignmentService kingdomAssignmentService) =>
                {
                    var user = await userManager.GetUserAsync(httpContext.User);
                    if (user is null)
                    {
                        return Results.LocalRedirect($"/Account/Login?ReturnUrl={Uri.EscapeDataString($"/organizace/hry/{gameId}/kralovstvi")}");
                    }

                    try
                    {
                        var form = await httpContext.Request.ReadFormAsync();
                        if (!int.TryParse(form["registrationId"], out var registrationId))
                        {
                            return Results.LocalRedirect($"/organizace/hry/{gameId}/kralovstvi?error={Uri.EscapeDataString("Neplatná registrace.")}");
                        }

                        int? kingdomId = int.TryParse(form["kingdomId"], out var kid) && kid > 0 ? kid : null;

                        await kingdomAssignmentService.AssignPlayerAsync(registrationId, kingdomId, user.Id, expectedGameId: gameId);
                        return Results.LocalRedirect($"/organizace/hry/{gameId}/kralovstvi?assigned=1");
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.LocalRedirect($"/organizace/hry/{gameId}/kralovstvi?error={Uri.EscapeDataString(ex.Message)}");
                    }
                })
            .RequireAuthorization(AuthorizationPolicies.StaffOnly);
        app.MapGet(
                "/api/registrations/person-suggestion",
                async (string? firstName, string? lastName, int? gameId, SubmissionService submissionService) =>
                {
                    if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName) || gameId is null)
                        return Results.BadRequest();

                    var suggestion = await submissionService.FindExistingPersonAsync(firstName, lastName, gameId.Value);
                    return suggestion is not null ? Results.Ok(suggestion) : Results.NotFound();
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
