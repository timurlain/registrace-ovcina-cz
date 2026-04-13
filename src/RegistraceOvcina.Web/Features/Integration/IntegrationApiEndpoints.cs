using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Roles;
using RegistraceOvcina.Web.Features.Users;

namespace RegistraceOvcina.Web.Features.Integration;

public static class IntegrationApiEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1")
            .AddEndpointFilter<ApiKeyEndpointFilter>()
            .WithTags("Integration API");

        // GET /api/v1/games — published games only
        group.MapGet("/games", async (
            IDbContextFactory<ApplicationDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var games = await db.Games
                .AsNoTracking()
                .Where(g => g.IsPublished)
                .OrderBy(g => g.StartsAtUtc)
                .Select(g => new GameDto(
                    g.Id,
                    g.Name,
                    g.Description,
                    g.StartsAtUtc,
                    g.EndsAtUtc,
                    g.RegistrationClosesAtUtc,
                    g.TargetPlayerCountTotal,
                    g.IsPublished))
                .ToListAsync(ct);

            return Results.Ok(games);
        }).AllowAnonymous();

        // GET /api/v1/games/{id}/registrations — active registrations for a game
        group.MapGet("/games/{id:int}/registrations", async (
            int id,
            IDbContextFactory<ApplicationDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var gameExists = await db.Games.AsNoTracking().AnyAsync(g => g.Id == id && g.IsPublished, ct);
            if (!gameExists)
                return Results.NotFound();

            var registrations = await db.Registrations
                .AsNoTracking()
                .Where(r =>
                    r.Submission.GameId == id &&
                    r.Submission.Status == SubmissionStatus.Submitted &&
                    r.Status == RegistrationStatus.Active &&
                    !r.Submission.IsDeleted)
                .Select(r => new RegistrationDto(
                    r.Id,
                    r.PersonId,
                    r.Person.FirstName,
                    r.Person.LastName,
                    r.Person.BirthYear,
                    r.AttendeeType.ToString(),
                    r.CharacterName,
                    r.Status.ToString()))
                .ToListAsync(ct);

            return Results.Ok(registrations);
        }).AllowAnonymous();

        // GET /api/v1/games/{id}/characters — character seeds for hra import
        // Source of truth: Registrations table with the same filters as the QR stickers page.
        // Only active player registrations are included. CharacterAppearance is joined
        // for race/class/kingdom/level if it exists for this registration.
        group.MapGet("/games/{id:int}/characters", async (
            int id,
            IDbContextFactory<ApplicationDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var characters = await db.Registrations
                .AsNoTracking()
                .Where(r => r.Submission.GameId == id
                    && r.Submission.Status == SubmissionStatus.Submitted
                    && r.Status == RegistrationStatus.Active
                    && r.AttendeeType == AttendeeType.Player
                    && !r.Submission.IsDeleted
                    && !r.Person.IsDeleted)
                .Select(r => new
                {
                    Registration = r,
                    Appearance = db.CharacterAppearances
                        .Where(ca => ca.RegistrationId == r.Id && !ca.Character.IsDeleted)
                        .Select(ca => new
                        {
                            ca.CharacterId,
                            ca.Character.Race,
                            ca.Character.ClassOrType,
                            KingdomName = ca.AssignedKingdom != null ? ca.AssignedKingdom.Name : null,
                            ca.AssignedKingdomId,
                            ca.LevelReached,
                            ContinuityStatus = ca.ContinuityStatus.ToString()
                        })
                        .FirstOrDefault()
                })
                .Select(x => new CharacterSeedDto(
                    x.Appearance != null ? x.Appearance.CharacterId : 0,
                    x.Registration.PersonId,
                    x.Registration.Person.FirstName,
                    x.Registration.Person.LastName,
                    x.Registration.Person.BirthYear,
                    x.Registration.CharacterName ?? (x.Registration.Person.FirstName + " " + x.Registration.Person.LastName),
                    x.Appearance != null ? x.Appearance.Race : null,
                    x.Appearance != null ? x.Appearance.ClassOrType : null,
                    x.Appearance != null ? x.Appearance.KingdomName : null,
                    x.Appearance != null ? x.Appearance.AssignedKingdomId : null,
                    x.Appearance != null ? x.Appearance.LevelReached : null,
                    x.Appearance != null ? x.Appearance.ContinuityStatus : "Unknown"))
                .ToListAsync(ct);

            return Results.Ok(characters);
        }).AllowAnonymous();

        // GET /api/v1/users/by-email?email=... — user existence check for OvčinaHra auth
        group.MapGet("/users/by-email", async (
            string email,
            IDbContextFactory<ApplicationDbContext> dbFactory,
            UserEmailService userEmailService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(email))
                return Results.BadRequest("email is required.");

            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var userId = await userEmailService.ResolveUserIdByEmailAsync(email, ct);

            if (userId is null)
                return Results.Ok(new { Exists = false, DisplayName = (string?)null, Roles = (List<string>?)null });

            var user = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.DisplayName, u.IsActive })
                .FirstOrDefaultAsync(ct);

            if (user is null || !user.IsActive)
                return Results.Ok(new { Exists = false, DisplayName = (string?)null, Roles = (List<string>?)null });

            var roles = await db.UserRoles
                .AsNoTracking()
                .Where(ur => ur.UserId == userId)
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                .ToListAsync(ct);

            return Results.Ok(new { Exists = true, DisplayName = user.DisplayName, Roles = roles });
        }).AllowAnonymous();

        // GET /api/v1/registrations/check?email=...&gameId=... — presence check
        group.MapGet("/registrations/check", async (
            string email,
            int gameId,
            IDbContextFactory<ApplicationDbContext> dbFactory,
            UserEmailService userEmailService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(email))
                return Results.BadRequest("email is required.");

            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var normalizedEmail = email.Trim().ToUpperInvariant();

            var isRegistered = await db.Registrations
                .AsNoTracking()
                .AnyAsync(r =>
                    r.Submission.GameId == gameId &&
                    r.Submission.Status == SubmissionStatus.Submitted &&
                    r.Status == RegistrationStatus.Active &&
                    !r.Submission.IsDeleted &&
                    r.Person.Email != null &&
                    r.Person.Email.ToUpper() == normalizedEmail,
                    ct);

            if (!isRegistered)
            {
                // Fallback: resolve user by primary + alternate emails, then check by PersonId
                var userId = await userEmailService.ResolveUserIdByEmailAsync(email, ct);
                if (userId is not null)
                {
                    var personId = await db.Users
                        .AsNoTracking()
                        .Where(u => u.Id == userId)
                        .Select(u => u.PersonId)
                        .FirstOrDefaultAsync(ct);

                    if (personId.HasValue)
                    {
                        isRegistered = await db.Registrations
                            .AsNoTracking()
                            .AnyAsync(r =>
                                r.Submission.GameId == gameId &&
                                r.Submission.Status == SubmissionStatus.Submitted &&
                                r.Status == RegistrationStatus.Active &&
                                !r.Submission.IsDeleted &&
                                r.PersonId == personId.Value,
                                ct);
                    }
                }
            }

            return Results.Ok(new PresenceCheckDto(isRegistered));
        }).AllowAnonymous();

        // GET /api/v1/users/{email}/roles?gameId={gameId} — game roles for a user
        group.MapGet("/users/{email}/roles", async (
            string email,
            int gameId,
            GameRoleService gameRoleService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(email))
                return Results.BadRequest("email is required.");

            var roles = await gameRoleService.GetRolesForUserAsync(email, gameId);
            return Results.Ok(new UserGameRolesDto(roles));
        }).AllowAnonymous();

        // GET /api/v1/users/{email}/has-role?role={role}&gameId={gameId} — role check
        group.MapGet("/users/{email}/has-role", async (
            string email,
            string role,
            int gameId,
            GameRoleService gameRoleService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(email))
                return Results.BadRequest("email is required.");
            if (string.IsNullOrWhiteSpace(role))
                return Results.BadRequest("role is required.");

            var hasRole = await gameRoleService.HasRoleAsync(email, gameId, role);
            return Results.Ok(new HasRoleDto(hasRole));
        }).AllowAnonymous();

        // POST /api/v1/users/{email}/roles — assign a game role
        group.MapPost("/users/{email}/roles", async (
            string email,
            AssignRoleRequest request,
            GameRoleService gameRoleService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(email))
                return Results.BadRequest("email is required.");
            if (string.IsNullOrWhiteSpace(request.RoleName))
                return Results.BadRequest("roleName is required.");

            try
            {
                await gameRoleService.AssignRoleAsync(email, request.GameId, request.RoleName, actorUserId: "api");
                return Results.Ok(new { assigned = true });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        // DELETE /api/v1/users/{email}/roles/{role}?gameId={gameId} — revoke a game role
        group.MapDelete("/users/{email}/roles/{role}", async (
            string email,
            string role,
            int gameId,
            GameRoleService gameRoleService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(email))
                return Results.BadRequest("email is required.");

            await gameRoleService.RevokeRoleAsync(email, gameId, role, actorUserId: "api");
            return Results.Ok(new { revoked = true });
        });

        return app;
    }
}

/// <summary>
/// Validates the X-Api-Key header against IntegrationApi:ApiKey config.
/// Returns 401 when the key is missing or wrong.
/// </summary>
internal sealed class ApiKeyEndpointFilter(IOptions<IntegrationApiOptions> options) : IEndpointFilter
{
    private const string ApiKeyHeader = "X-Api-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configuredKey = options.Value.ApiKey;

        // If no key is configured, block all requests (fail-safe)
        if (string.IsNullOrWhiteSpace(configuredKey))
            return Results.Problem("Integration API is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey)
            || !string.Equals(providedKey, configuredKey, StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
