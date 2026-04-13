using System.Text.Json;
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

        // GET /api/v1/games/{id}/info — full game info with parsed organization metadata
        group.MapGet("/games/{id:int}/info", async (
            int id,
            IDbContextFactory<ApplicationDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var game = await db.Games.AsNoTracking()
                .Where(g => g.Id == id && g.IsPublished)
                .FirstOrDefaultAsync(ct);

            if (game is null)
                return Results.NotFound();

            // Parse OrganizationInfo JSON if present
            JsonElement? orgInfo = null;
            if (!string.IsNullOrWhiteSpace(game.OrganizationInfo))
            {
                try
                {
                    using var doc = JsonDocument.Parse(game.OrganizationInfo);
                    orgInfo = doc.RootElement.Clone();
                }
                catch (JsonException)
                {
                    // Malformed JSON — return null
                }
            }

            var totalRegistered = await db.Registrations.AsNoTracking()
                .CountAsync(r => r.Submission.GameId == id
                    && r.Submission.Status == SubmissionStatus.Submitted
                    && r.Status == RegistrationStatus.Active
                    && !r.Submission.IsDeleted, ct);

            return Results.Ok(new GameInfoDto(
                game.Id,
                game.Name,
                game.Description,
                game.StartsAtUtc,
                game.EndsAtUtc,
                game.RegistrationClosesAtUtc,
                game.TargetPlayerCountTotal,
                totalRegistered,
                game.IsPublished,
                orgInfo));
        }).AllowAnonymous();

        // GET /api/v1/users/{email}/game-info?gameId=... — comprehensive user-in-game info
        group.MapGet("/users/{email}/game-info", async (
            string email,
            int gameId,
            IDbContextFactory<ApplicationDbContext> dbFactory,
            UserEmailService userEmailService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(email))
                return Results.BadRequest("email is required.");

            var info = await LoadUserGameInfoAsync(email, gameId, dbFactory, userEmailService, ct);
            return Results.Ok(info);
        });

        // GET /api/v1/users/{email}/lodging?gameId=... — lodging slice only
        group.MapGet("/users/{email}/lodging", async (
            string email,
            int gameId,
            IDbContextFactory<ApplicationDbContext> dbFactory,
            UserEmailService userEmailService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(email))
                return Results.BadRequest("email is required.");

            var info = await LoadUserGameInfoAsync(email, gameId, dbFactory, userEmailService, ct);
            return Results.Ok(info.Lodging);
        });

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
        group.MapGet("/games/{id:int}/characters", async (
            int id,
            IDbContextFactory<ApplicationDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var characters = await db.CharacterAppearances
                .AsNoTracking()
                .Where(ca => ca.GameId == id && !ca.Character.IsDeleted)
                .Select(ca => new CharacterSeedDto(
                    ca.CharacterId,
                    ca.Character.PersonId,
                    ca.Character.Person.FirstName,
                    ca.Character.Person.LastName,
                    ca.Character.Person.BirthYear,
                    ca.Character.Name,
                    ca.Character.Race,
                    ca.Character.ClassOrType,
                    ca.AssignedKingdom != null ? ca.AssignedKingdom.Name : null,
                    ca.AssignedKingdomId,
                    ca.LevelReached,
                    ca.ContinuityStatus.ToString()))
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

    private static async Task<UserGameInfoDto> LoadUserGameInfoAsync(
        string email,
        int gameId,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        UserEmailService userEmailService,
        CancellationToken ct)
    {
        var userId = await userEmailService.ResolveUserIdByEmailAsync(email, ct);
        if (userId is null)
        {
            return new UserGameInfoDto(email, gameId, false, null, null, "unpaid", null, null, [], null, []);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var submission = await db.RegistrationSubmissions.AsNoTracking()
            .Where(s => s.RegistrantUserId == userId && s.GameId == gameId && !s.IsDeleted)
            .OrderByDescending(s => s.SubmittedAtUtc ?? s.LastEditedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (submission is null)
        {
            return new UserGameInfoDto(email, gameId, false, null, null, "unpaid", null, null, [], null, []);
        }

        // Load attendees with associated person + kingdom + room data
        var attendees = await db.Registrations.AsNoTracking()
            .Where(r => r.SubmissionId == submission.Id && r.Status == RegistrationStatus.Active)
            .OrderBy(r => r.Person.LastName).ThenBy(r => r.Person.FirstName)
            .Select(r => new
            {
                RegistrationId = r.Id,
                r.PersonId,
                r.Person.FirstName,
                r.Person.LastName,
                r.Person.BirthYear,
                r.AttendeeType,
                r.CharacterName,
                r.PreferredKingdomId,
                r.AssignedGameRoomId
            })
            .ToListAsync(ct);

        var attendeeDtos = attendees.Select(a => new UserAttendeeDto(
            a.PersonId,
            a.FirstName,
            a.LastName,
            a.BirthYear,
            a.AttendeeType.ToString(),
            a.CharacterName,
            a.PreferredKingdomId
        )).ToList();

        // Compute payment totals + status
        var paid = await db.Payments.AsNoTracking()
            .Where(p => p.SubmissionId == submission.Id)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        var expected = submission.ExpectedTotalAmount;
        string paymentStatus = expected > 0 && paid >= expected ? "paid"
            : paid > 0 ? "partial"
            : "unpaid";

        // Resolve the registrant's own attendee record (by user's PersonId link)
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.PersonId })
            .FirstOrDefaultAsync(ct);
        var selfPersonId = user?.PersonId;

        var selfReg = attendees.FirstOrDefault(a => selfPersonId.HasValue && a.PersonId == selfPersonId.Value)
            ?? attendees.FirstOrDefault(a => a.AttendeeType == AttendeeType.Adult)
            ?? attendees.FirstOrDefault();

        UserLodgingDto? lodging = null;
        if (selfReg?.AssignedGameRoomId is int roomId)
        {
            var room = await db.GameRooms.AsNoTracking()
                .Where(gr => gr.Id == roomId)
                .Select(gr => new { gr.Id, RoomName = gr.Room.Name, gr.Capacity })
                .FirstOrDefaultAsync(ct);

            if (room is not null)
            {
                var roommates = await db.Registrations.AsNoTracking()
                    .Where(r => r.AssignedGameRoomId == room.Id
                        && r.Submission.GameId == gameId
                        && r.Status == RegistrationStatus.Active
                        && !r.Submission.IsDeleted
                        && r.PersonId != selfReg.PersonId)
                    .Select(r => r.Person.FirstName + " " + r.Person.LastName)
                    .ToListAsync(ct);

                lodging = new UserLodgingDto("Indoor", room.RoomName, room.Capacity, roommates);
            }
        }

        // Game roles for this user in this game
        var gameRoles = await db.GameRoles.AsNoTracking()
            .Where(gr => gr.UserId == userId && gr.GameId == gameId)
            .Select(gr => gr.RoleName)
            .ToListAsync(ct);

        return new UserGameInfoDto(
            email,
            gameId,
            true,
            submission.GroupName,
            submission.SubmittedAtUtc,
            paymentStatus,
            expected,
            paid,
            attendeeDtos,
            lodging,
            gameRoles);
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
