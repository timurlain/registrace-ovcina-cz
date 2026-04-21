using System.Globalization;
using System.Text;
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
        // Source of truth: Registrations table with the same filters as the QR stickers page.
        // Only active player registrations are included. CharacterAppearance is joined
        // for race/class/kingdom/level if it exists for this registration.
        group.MapGet("/games/{id:int}/characters", async (
            int id,
            IDbContextFactory<ApplicationDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var gameExists = await db.Games.AsNoTracking().AnyAsync(g => g.Id == id && g.IsPublished, ct);
            if (!gameExists)
                return Results.NotFound();

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
                    // Scoped to this game and deterministically ordered so the selected
                    // appearance is stable when multiple rows exist for a registration.
                    Appearance = db.CharacterAppearances
                        .Where(ca => ca.RegistrationId == r.Id
                            && ca.GameId == id
                            && !ca.Character.IsDeleted)
                        .OrderBy(ca => ca.CharacterId)
                        .ThenBy(ca => ca.Id)
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
                    x.Appearance != null ? (int?)x.Appearance.CharacterId : null,
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

        // GET /api/v1/games/{id}/adults — active adult attendees with email + game roles
        group.MapGet("/games/{id:int}/adults", async (
            int id,
            IDbContextFactory<ApplicationDbContext> dbFactory,
            CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var gameExists = await db.Games.AsNoTracking().AnyAsync(g => g.Id == id, ct);
            if (!gameExists)
                return Results.NotFound();

            var adults = await LoadAdultsAsync(db, id, ct);
            return Results.Ok(adults);
        });

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
                return Results.Ok(new { Exists = false, DisplayName = (string?)null, PersonId = (int?)null, Roles = (List<string>?)null });

            var user = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.DisplayName, u.IsActive, u.PersonId })
                .FirstOrDefaultAsync(ct);

            if (user is null || !user.IsActive)
                return Results.Ok(new { Exists = false, DisplayName = (string?)null, PersonId = (int?)null, Roles = (List<string>?)null });

            var roles = await db.UserRoles
                .AsNoTracking()
                .Where(ur => ur.UserId == userId)
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                .ToListAsync(ct);

            return Results.Ok(new { Exists = true, DisplayName = user.DisplayName, user.PersonId, Roles = roles });
        }).AllowAnonymous();

        // GET /api/v1/users/{email}/person-id — quick email→personId resolver for hra/bot
        group.MapGet("/users/{email}/person-id", async (
            string email,
            IDbContextFactory<ApplicationDbContext> dbFactory,
            UserEmailService userEmailService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(email))
                return Results.BadRequest("email is required.");

            var userId = await userEmailService.ResolveUserIdByEmailAsync(email, ct);
            if (userId is null) return Results.NotFound();

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var personId = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.PersonId)
                .FirstOrDefaultAsync(ct);

            if (personId is null) return Results.NotFound();
            return Results.Ok(new { PersonId = personId.Value });
        });

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
            var result = await CheckRegistrationPresenceAsync(db, email, gameId, ct);
            return Results.Ok(result);
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

    /// <summary>
    /// Presence check for GET /api/v1/registrations/check. Returns
    /// <see cref="PresenceCheckDto"/> describing whether the given email is associated
    /// with the given game, either as an attendee Registration (any
    /// <see cref="AttendeeType"/> — Player or Adult) or as the PrimaryEmail on a
    /// submitted <see cref="RegistrationSubmission"/> (household contact).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Historically the handler only matched <c>Person.Email</c>, which returned
    /// <c>isRegistered:false</c> for adults whose <c>Person.Email</c> was cleared
    /// during a dedup/import (e.g. because it duplicated the submission's primary
    /// email) and who had no linked <see cref="ApplicationUser"/>.
    /// </para>
    /// <para>
    /// The method now unions four signals, all case-insensitive and
    /// whitespace-trimmed on both sides, all scoped to the given game and
    /// honouring soft-delete / status filters:
    /// </para>
    /// <list type="number">
    /// <item><description><c>Person.Email</c> on any active Registration whose
    /// submission is Submitted.</description></item>
    /// <item><description><c>ApplicationUser.NormalizedEmail</c> (primary or alias
    /// in <c>UserEmails</c>) → <c>PersonId</c> → any active Registration.</description></item>
    /// <item><description>Primary-contact-name match: <c>Submission.PrimaryEmail</c>
    /// matches the input AND <c>Submission.PrimaryContactName</c> matches the
    /// First+LastName of an Adult attendee in the same submission (case-insensitive,
    /// diacritic-normalized). Catches the case where dedup nulled <c>Person.Email</c>
    /// and no <c>ApplicationUser</c> link exists — the submission itself tells us
    /// the primary contact is also an attendee.</description></item>
    /// <item><description><c>RegistrationSubmission.PrimaryEmail</c> on any
    /// submitted, non-deleted submission (guardian fallback).</description></item>
    /// </list>
    /// <para>
    /// <c>guardianOnly</c> is set when only signal 4 matches — i.e. the
    /// caller registered their kids but did not register themselves as an
    /// attendee. This lets bot clients distinguish "registered themselves"
    /// from "registered their household".
    /// </para>
    /// </remarks>
    internal static async Task<PresenceCheckDto> CheckRegistrationPresenceAsync(
        ApplicationDbContext db,
        string email,
        int gameId,
        CancellationToken ct)
    {
        // Trim + upper-case (culture-invariant) for case-insensitive comparison on
        // the SQL side. PostgreSQL's UPPER() is collation-aware; for ASCII emails
        // it matches ToUpperInvariant(). We ALSO trim Person.Email / PrimaryEmail
        // on the SQL side because historical data has been observed with trailing
        // whitespace introduced by import scripts.
        var normalizedEmail = email.Trim().ToUpperInvariant();

        // ---------------------------------------------------------------
        // Signal A — own-Registration match (strongest): the email resolves
        // to a Person who has their own active Registration in this game.
        //
        // This is computed ALWAYS (not gated behind Signal A.1 failing) and
        // unions three email→Person paths, so we don't miss edge cases where
        // one path fails (e.g. duplicate ApplicationUser rows with the same
        // NormalizedEmail, or a dedup pass that nulled Person.Email but left
        // UserEmails alone). Mirrors how LoadUserGameInfoAsync resolves the
        // caller: email → ApplicationUser → PersonId → Registration.
        // ---------------------------------------------------------------

        // A.1 — direct Person.Email match on a Registration in this game.
        var attendeeByPersonEmail = await db.Registrations
            .AsNoTracking()
            .AnyAsync(r =>
                r.Submission.GameId == gameId &&
                r.Submission.Status == SubmissionStatus.Submitted &&
                r.Status == RegistrationStatus.Active &&
                !r.Submission.IsDeleted &&
                r.Person.Email != null &&
                r.Person.Email.Trim().ToUpperInvariant() == normalizedEmail,
                ct);

        // A.2 — email resolves to any ApplicationUser (primary NormalizedEmail
        // or an alias in UserEmails) whose PersonId has an active Registration
        // in this game. We collect ALL matching user ids (defensive against
        // duplicate rows from historic imports) and project through to
        // PersonId values, so a single duplicate doesn't make us miss the link.
        var matchingUserIds = await db.Users
            .AsNoTracking()
            .Where(u => u.NormalizedEmail == normalizedEmail)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var aliasUserIds = await db.UserEmails
            .AsNoTracking()
            .Where(ue => ue.NormalizedEmail == normalizedEmail)
            .Select(ue => ue.UserId)
            .ToListAsync(ct);

        var candidateUserIds = matchingUserIds
            .Concat(aliasUserIds)
            .Distinct()
            .ToList();

        var personIdsFromUsers = candidateUserIds.Count == 0
            ? new List<int>()
            : await db.Users
                .AsNoTracking()
                .Where(u => candidateUserIds.Contains(u.Id) && u.PersonId != null)
                .Select(u => u.PersonId!.Value)
                .Distinct()
                .ToListAsync(ct);

        var attendeeByUserLink = personIdsFromUsers.Count > 0
            && await db.Registrations
                .AsNoTracking()
                .AnyAsync(r =>
                    r.Submission.GameId == gameId &&
                    r.Submission.Status == SubmissionStatus.Submitted &&
                    r.Status == RegistrationStatus.Active &&
                    !r.Submission.IsDeleted &&
                    personIdsFromUsers.Contains(r.PersonId),
                    ct);

        // ---------------------------------------------------------------
        // Signal C — primary-contact-name match (fallback for dedup orphans):
        // For the Lukáš Heinz real-data case — Person.Email was nulled by dedup
        // and no ApplicationUser links the email to the Person. The only
        // remaining signal is the submission itself: Submission.PrimaryEmail
        // matches the input, AND Submission.PrimaryContactName matches the
        // First+LastName of an Adult attendee in that same submission.
        //
        // If both hold, the primary-contact is ALSO attending → own-registration.
        // Requires BOTH firstName AND lastName to match (diacritic-insensitive,
        // trim+lowercase) to avoid false positives like "Klára Heinzová".
        // Only Adult attendees count: a parent named same as their child must
        // NOT flip the flag (handled by the AttendeeType.Adult filter below).
        // ---------------------------------------------------------------
        var attendeeByPrimaryContactName = false;
        if (!attendeeByPersonEmail && !attendeeByUserLink)
        {
            // Load primary-contact rows whose PrimaryEmail matches, together
            // with this submission's Adult attendees' names. Small result set
            // (usually 0 or 1 submission per email), so we evaluate the name
            // comparison in memory with diacritic normalization.
            var primaryContactCandidates = await db.RegistrationSubmissions
                .AsNoTracking()
                .Where(s =>
                    s.GameId == gameId &&
                    s.Status == SubmissionStatus.Submitted &&
                    !s.IsDeleted &&
                    s.PrimaryEmail.Trim().ToUpperInvariant() == normalizedEmail)
                .Select(s => new
                {
                    s.PrimaryContactName,
                    AdultAttendees = s.Registrations
                        .Where(r =>
                            r.Status == RegistrationStatus.Active &&
                            r.AttendeeType == AttendeeType.Adult)
                        .Select(r => new
                        {
                            r.Person.FirstName,
                            r.Person.LastName
                        })
                        .ToList()
                })
                .ToListAsync(ct);

            foreach (var candidate in primaryContactCandidates)
            {
                if (string.IsNullOrWhiteSpace(candidate.PrimaryContactName))
                    continue;

                var (contactFirst, contactLast) = SplitContactName(candidate.PrimaryContactName);
                if (contactFirst is null || contactLast is null)
                    continue;

                var normContactFirst = NormalizeForNameCompare(contactFirst);
                var normContactLast = NormalizeForNameCompare(contactLast);

                foreach (var attendee in candidate.AdultAttendees)
                {
                    if (NormalizeForNameCompare(attendee.FirstName) == normContactFirst &&
                        NormalizeForNameCompare(attendee.LastName) == normContactLast)
                    {
                        attendeeByPrimaryContactName = true;
                        break;
                    }
                }

                if (attendeeByPrimaryContactName)
                    break;
            }
        }

        var hasOwnRegistrationForThisPerson =
            attendeeByPersonEmail || attendeeByUserLink || attendeeByPrimaryContactName;

        // ---------------------------------------------------------------
        // Signal D — primary-email match (guardian fallback): email matches a
        // submission's PrimaryEmail on a submitted, non-deleted submission in
        // this game. Parents who registered their kids but not themselves
        // still show up as "registered", flagged guardianOnly.
        // ---------------------------------------------------------------
        var hasPrimaryContactMatch = await db.RegistrationSubmissions
            .AsNoTracking()
            .AnyAsync(s =>
                s.GameId == gameId &&
                s.Status == SubmissionStatus.Submitted &&
                !s.IsDeleted &&
                s.PrimaryEmail.Trim().ToUpperInvariant() == normalizedEmail,
                ct);

        // ---------------------------------------------------------------
        // Verdict — own-Registration ALWAYS wins over primary-contact-only.
        // ---------------------------------------------------------------
        var isRegistered = hasOwnRegistrationForThisPerson || hasPrimaryContactMatch;
        var guardianOnly = !hasOwnRegistrationForThisPerson && hasPrimaryContactMatch;

        return new PresenceCheckDto(isRegistered, guardianOnly);
    }

    /// <summary>
    /// Splits a free-form "First [Middle...] Last" contact name into a first
    /// name (first token) and last name (last token). Returns (null, null) if
    /// the input has fewer than 2 non-whitespace tokens.
    /// </summary>
    private static (string? First, string? Last) SplitContactName(string name)
    {
        var tokens = name.Trim().Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
            return (null, null);
        return (tokens[0], tokens[^1]);
    }

    /// <summary>
    /// Trims, lowercases (invariant), and strips combining diacritic marks so
    /// that "Lukáš" and "Lukas" compare equal. Used only for comparing the
    /// primary contact name against an attendee's First/LastName — NOT for
    /// storage or email normalization.
    /// </summary>
    private static string NormalizeForNameCompare(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;

        var trimmed = s.Trim().ToLowerInvariant();
        var decomposed = trimmed.Normalize(NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                builder.Append(ch);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Loads the distinct adult attendees for a game (active registrations on submitted,
    /// non-deleted submissions) together with their contact email and any game-role
    /// assignments. Dedup is pushed to the database so we only materialize one row per
    /// <c>PersonId</c>. Email is taken from the linked <see cref="ApplicationUser"/>
    /// when present and falls back to <see cref="Person.Email"/>, because
    /// <c>Person.Email</c> is intentionally left null when the contact email matches the
    /// submission's primary email. Factored out of the endpoint lambda so it can be
    /// unit-tested against an in-memory DB.
    /// </summary>
    internal static async Task<List<AdultDto>> LoadAdultsAsync(
        ApplicationDbContext db,
        int gameId,
        CancellationToken ct)
    {
        // Distinct adults from the DB — group by PersonId so an adult listed in several
        // submissions is materialized as a single row.
        var distinctAdults = await db.Registrations
            .AsNoTracking()
            .Where(r =>
                r.Submission.GameId == gameId &&
                r.Submission.Status == SubmissionStatus.Submitted &&
                r.Status == RegistrationStatus.Active &&
                !r.Submission.IsDeleted &&
                r.AttendeeType == AttendeeType.Adult)
            .GroupBy(r => r.PersonId)
            .Select(g => new
            {
                PersonId = g.Key,
                FirstName = g.Select(r => r.Person.FirstName).First(),
                LastName = g.Select(r => r.Person.LastName).First(),
                BirthYear = g.Select(r => r.Person.BirthYear).First(),
                PersonEmail = g.Select(r => r.Person.Email).First()
            })
            .ToListAsync(ct);

        if (distinctAdults.Count == 0)
            return [];

        var personIds = distinctAdults.Select(a => a.PersonId).ToList();

        // Email fallback: ApplicationUser.Email (linked via PersonId) takes precedence
        // over Person.Email, which is often null because it's only set when it differs
        // from the submission's primary contact email.
        var userEmailByPerson = await db.Users
            .AsNoTracking()
            .Where(u => u.PersonId != null && personIds.Contains(u.PersonId!.Value) && u.IsActive)
            .GroupBy(u => u.PersonId!.Value)
            .Select(g => new { PersonId = g.Key, Email = g.Select(u => u.Email).First() })
            .ToListAsync(ct);
        var userEmailLookup = userEmailByPerson.ToDictionary(x => x.PersonId, x => x.Email);

        var roleRows = await db.GameRoles
            .AsNoTracking()
            .Where(gr => gr.GameId == gameId && gr.User.PersonId != null && personIds.Contains(gr.User.PersonId!.Value))
            .Select(gr => new { PersonId = gr.User.PersonId!.Value, gr.RoleName })
            .ToListAsync(ct);

        var rolesByPerson = roleRows
            .GroupBy(x => x.PersonId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.RoleName).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList());

        return distinctAdults
            .OrderBy(a => a.LastName, StringComparer.Ordinal)
            .ThenBy(a => a.FirstName, StringComparer.Ordinal)
            .Select(a => new AdultDto(
                a.PersonId,
                a.FirstName,
                a.LastName,
                a.BirthYear,
                userEmailLookup.TryGetValue(a.PersonId, out var userEmail) && !string.IsNullOrWhiteSpace(userEmail)
                    ? userEmail
                    : a.PersonEmail,
                rolesByPerson.TryGetValue(a.PersonId, out var roles) ? roles : []))
            .ToList();
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
