using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Components;
using RegistraceOvcina.Web.Components.Account;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Email;
using RegistraceOvcina.Web.Features.Games;
using RegistraceOvcina.Web.Features.Payments;
using RegistraceOvcina.Web.Features.Submissions;
using RegistraceOvcina.Web.Security;

namespace RegistraceOvcina.Web;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
        {
            builder.WebHost.UseStaticWebAssets();
        }

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<IdentityRedirectManager>();
        builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();

        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
        });
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownNetworks.Clear();
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
        builder.Services.AddScoped<GameService>();
        builder.Services.AddScoped<SubmissionService>();

        var app = builder.Build();

        app.UseForwardedHeaders();

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
