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
    public static void MapAuthorizationEndpoints(this WebApplication app)
    {
        app.MapGet("/connect/authorize", (Delegate)HandleAuthorize);
        app.MapPost("/connect/authorize", (Delegate)HandleAuthorize);
        app.MapPost("/connect/token", (Delegate)HandleToken);
        app.MapGet("/connect/userinfo", (Delegate)HandleUserinfo);
        app.MapPost("/connect/logout", (Delegate)HandleLogout);
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
        identity.SetClaim(Claims.Name, user.DisplayName);
        identity.SetClaim(Claims.Email, await userManager.GetEmailAsync(user));

        var roles = await userManager.GetRolesAsync(user);
        foreach (var role in roles)
        {
            identity.AddClaim(Claims.Role, role);
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
                    foreach (var role in freshRoles)
                        identity.AddClaim(Claims.Role, role);
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

        return Results.Ok(new
        {
            sub = principal.GetClaim(Claims.Subject),
            name = principal.GetClaim(Claims.Name),
            email = principal.GetClaim(Claims.Email),
            role = principal.GetClaims(Claims.Role),
        });
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
        return claim.Type switch
        {
            Claims.Name or Claims.Email or Claims.Role
                => [Destinations.AccessToken, Destinations.IdentityToken],
            Claims.Subject
                => [Destinations.AccessToken, Destinations.IdentityToken],
            _ => [Destinations.AccessToken],
        };
    }
}
