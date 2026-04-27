using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using RegistraceOvcina.Web.Data;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace RegistraceOvcina.Web.Endpoints;

public static class AuthorizationEndpoints
{
    public const string OidcSpaCorsPolicy = "OidcSpa";

    public static void MapAuthorizationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/connect").RequireCors(OidcSpaCorsPolicy);
        group.MapGet("/authorize", (Delegate)HandleAuthorize);
        group.MapPost("/authorize", (Delegate)HandleAuthorize);
        group.MapPost("/token", (Delegate)HandleToken);
        group.MapGet("/userinfo", (Delegate)HandleUserinfo);
        group.MapPost("/logout", (Delegate)HandleLogout);
    }

    private static async Task<IResult> HandleAuthorize(HttpContext context)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict request cannot be retrieved.");

        var result = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (result is not { Succeeded: true })
        {
            return Results.Challenge(
                authenticationSchemes: [IdentityConstants.ApplicationScheme],
                properties: new AuthenticationProperties
                {
                    RedirectUri = context.Request.PathBase + context.Request.Path +
                        QueryString.Create(
                            context.Request.HasFormContentType
                                ? context.Request.Form.Select(kvp => new KeyValuePair<string, string?>(kvp.Key, kvp.Value))
                                : context.Request.Query.Select(kvp => new KeyValuePair<string, string?>(kvp.Key, kvp.Value)))
                });
        }

        var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.GetUserAsync(result.Principal)
            ?? throw new InvalidOperationException("The user details cannot be retrieved.");

        var identity = new ClaimsIdentity(
            authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, await userManager.GetUserIdAsync(user));

        if (request.HasScope(Scopes.Profile))
            identity.SetClaim(Claims.Name, user.DisplayName);

        if (request.HasScope(Scopes.Email))
            identity.SetClaim(Claims.Email, await userManager.GetEmailAsync(user));

        if (request.HasScope("roles") || request.HasScope("organizer"))
        {
            var roles = await userManager.GetRolesAsync(user);

            if (request.HasScope("roles"))
            {
                foreach (var role in roles)
                    identity.AddClaim(Claims.Role, role);
            }

            if (request.HasScope("organizer") && roles.Contains(Security.RoleNames.Organizer))
                identity.AddClaim(Claims.Role, "organizer");
        }

        identity.SetScopes(request.GetScopes());
        identity.SetDestinations(GetDestinations);

        return Results.SignIn(
            new ClaimsPrincipal(identity),
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> HandleToken(HttpContext context)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict request cannot be retrieved.");

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var identity = result.Principal?.Identity as ClaimsIdentity
                ?? throw new InvalidOperationException("The claims identity cannot be retrieved.");

            var hasRolesScope = identity.HasScope("roles");
            var hasOrganizerScope = identity.HasScope("organizer");
            if (hasRolesScope || hasOrganizerScope)
            {
                var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                var userId = identity.GetClaim(Claims.Subject);
                if (userId is not null)
                {
                    var user = await userManager.FindByIdAsync(userId);
                    if (user is not null)
                    {
                        var existingRoles = identity.FindAll(Claims.Role).ToList();
                        foreach (var claim in existingRoles)
                            identity.RemoveClaim(claim);

                        var freshRoles = await userManager.GetRolesAsync(user);

                        if (hasRolesScope)
                        {
                            foreach (var role in freshRoles)
                                identity.AddClaim(Claims.Role, role);
                        }

                        if (hasOrganizerScope && freshRoles.Contains(Security.RoleNames.Organizer))
                            identity.AddClaim(Claims.Role, "organizer");
                    }
                }
            }

            identity.SetDestinations(GetDestinations);

            return Results.SignIn(
                new ClaimsPrincipal(identity),
                authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new InvalidOperationException("The specified grant type is not supported.");
    }

    private static async Task<IResult> HandleUserinfo(HttpContext context)
    {
        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = result.Principal
            ?? throw new InvalidOperationException("The claims principal cannot be retrieved.");

        var response = new Dictionary<string, object?>
        {
            ["sub"] = principal.GetClaim(Claims.Subject),
        };

        if (principal.HasScope(Scopes.Profile))
            response["name"] = principal.GetClaim(Claims.Name);

        if (principal.HasScope(Scopes.Email))
            response["email"] = principal.GetClaim(Claims.Email);

        if (principal.HasScope("roles") || principal.HasScope("organizer"))
            response["role"] = principal.GetClaims(Claims.Role);

        return Results.Ok(response);
    }

    private static async Task<IResult> HandleLogout(HttpContext context)
    {
        await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        return Results.SignOut(
            properties: null,
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        switch (claim.Type)
        {
            case Claims.Subject:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            case Claims.Name when claim.Subject?.HasScope(Scopes.Profile) == true:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            case Claims.Email when claim.Subject?.HasScope(Scopes.Email) == true:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            case Claims.Role when claim.Subject?.HasScope("roles") == true
                              || claim.Subject?.HasScope("organizer") == true:
                yield return Destinations.AccessToken;
                yield return Destinations.IdentityToken;
                yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
