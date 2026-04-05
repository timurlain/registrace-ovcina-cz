using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Data;

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

        // GET /api/v1/registrations/check?email=...&gameId=... — presence check
        group.MapGet("/registrations/check", async (
            string email,
            int gameId,
            IDbContextFactory<ApplicationDbContext> dbFactory,
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

            return Results.Ok(new PresenceCheckDto(isRegistered));
        }).AllowAnonymous();

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
