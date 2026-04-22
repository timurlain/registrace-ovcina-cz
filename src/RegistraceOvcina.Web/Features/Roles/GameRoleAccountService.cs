using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Security;

namespace RegistraceOvcina.Web.Features.Roles;

/// <summary>
/// Outcome of <see cref="GameRoleAccountService.LinkOrCreateAccountAsync"/> for a single Person.
/// </summary>
public enum LinkOrCreateOutcome
{
    /// <summary>An existing ApplicationUser was found by exact email match and <c>PersonId</c> was set on it.</summary>
    Linked,

    /// <summary>A new stub ApplicationUser was created (EmailConfirmed=false, random password) and <c>PersonId</c> was set.</summary>
    Created,

    /// <summary>The Person has no email (or is soft-deleted) — nothing was done. Organizer must fill the email in Person detail first.</summary>
    MissingEmail,

    /// <summary>The Person already has a linked ApplicationUser — no-op.</summary>
    AlreadyLinked,

    /// <summary>An ApplicationUser with that email exists but is already linked to a different Person. No change made.</summary>
    ConflictEmailUsedByAnotherPerson
}

/// <summary>Result of <see cref="GameRoleAccountService.LinkOrCreateAccountAsync"/>.</summary>
public sealed record LinkOrCreateResult(LinkOrCreateOutcome Outcome, string? UserId, string? Message);

/// <summary>Aggregated outcome of <see cref="GameRoleAccountService.LinkOrCreateAccountsForGameAsync"/>.</summary>
public sealed record BulkLinkOrCreateResult(
    int Linked,
    int Created,
    int MissingEmail,
    int Conflicts,
    int AlreadyLinked,
    IReadOnlyList<string> FirstErrors);

/// <summary>
/// Abstraction for the "create a new stub ApplicationUser" step. In production this is backed
/// by <see cref="UserManager{TUser}"/>; in tests we stub it with a DbContext-only implementation.
/// </summary>
public interface IStubAccountCreator
{
    Task<StubAccountCreateResult> CreateStubAsync(
        string email,
        string displayName,
        CancellationToken ct);
}

public sealed record StubAccountCreateResult(bool Succeeded, string? UserId, string? ErrorMessage);

public interface IGameRoleAccountService
{
    Task<LinkOrCreateResult> LinkOrCreateAccountAsync(int personId, string actorUserId, CancellationToken ct);

    Task<BulkLinkOrCreateResult> LinkOrCreateAccountsForGameAsync(
        int gameId,
        string actorUserId,
        CancellationToken ct);
}

public sealed class GameRoleAccountService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IStubAccountCreator stubCreator,
    TimeProvider timeProvider,
    ILogger<GameRoleAccountService> logger)
    : IGameRoleAccountService
{
    public async Task<LinkOrCreateResult> LinkOrCreateAccountAsync(
        int personId,
        string actorUserId,
        CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // 1. Load Person. Soft-deleted rows are also treated as "not eligible" — return MissingEmail.
        var person = await db.People.AsNoTracking()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(p => p.Id == personId, ct);

        if (person is null || person.IsDeleted || string.IsNullOrWhiteSpace(person.Email))
        {
            return new LinkOrCreateResult(LinkOrCreateOutcome.MissingEmail, null,
                "Osoba nemá vyplněný e-mail.");
        }

        // 2. Already linked? Skip.
        var alreadyLinkedUser = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.PersonId == personId, ct);
        if (alreadyLinkedUser is not null)
        {
            return new LinkOrCreateResult(LinkOrCreateOutcome.AlreadyLinked, alreadyLinkedUser.Id,
                "Osoba už má propojený účet.");
        }

        var trimmedEmail = person.Email.Trim();
        var normalizedEmail = trimmedEmail.ToUpperInvariant();
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        // 3. Look up ApplicationUser by NormalizedEmail.
        var matchingUser = await db.Users
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

        if (matchingUser is not null)
        {
            // 3a. Exists and unlinked → link.
            if (matchingUser.PersonId is null)
            {
                matchingUser.PersonId = personId;

                db.AuditLogs.Add(new AuditLog
                {
                    EntityType = nameof(ApplicationUser),
                    EntityId = matchingUser.Id,
                    Action = "LinkAccount",
                    ActorUserId = actorUserId,
                    CreatedAtUtc = nowUtc,
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        PersonId = personId,
                        PersonName = FullName(person.FirstName, person.LastName),
                        Signal = "ExactEmailMatch",
                        Source = "LinkOrCreate"
                    })
                });

                await db.SaveChangesAsync(ct);
                logger.LogInformation(
                    "LinkOrCreate: linked existing user {UserId} to person {PersonId} (actor {ActorId}).",
                    matchingUser.Id, personId, actorUserId);

                return new LinkOrCreateResult(LinkOrCreateOutcome.Linked, matchingUser.Id,
                    "Účet propojen.");
            }

            // 3b. Exists, linked to a different Person → conflict, do not modify.
            if (matchingUser.PersonId != personId)
            {
                logger.LogWarning(
                    "LinkOrCreate: email {Email} already belongs to user {UserId} linked to a different person {OtherPersonId} (requested by actor {ActorId} for person {PersonId}).",
                    trimmedEmail, matchingUser.Id, matchingUser.PersonId, actorUserId, personId);

                return new LinkOrCreateResult(LinkOrCreateOutcome.ConflictEmailUsedByAnotherPerson,
                    matchingUser.Id,
                    "E-mail patří jiné osobě.");
            }

            // Defensive: matches personId, treat as already-linked.
            return new LinkOrCreateResult(LinkOrCreateOutcome.AlreadyLinked, matchingUser.Id,
                "Osoba už má propojený účet.");
        }

        // 4. No matching user — create a new stub.
        var displayName = FullName(person.FirstName, person.LastName);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = trimmedEmail;
        }

        var createResult = await stubCreator.CreateStubAsync(trimmedEmail, displayName, ct);
        if (!createResult.Succeeded || createResult.UserId is null)
        {
            logger.LogWarning(
                "LinkOrCreate: stub account creation failed for person {PersonId} (email {Email}): {Error}",
                personId, trimmedEmail, createResult.ErrorMessage);

            return new LinkOrCreateResult(LinkOrCreateOutcome.ConflictEmailUsedByAnotherPerson, null,
                createResult.ErrorMessage ?? "Nelze vytvořit účet.");
        }

        // Now load the fresh user, link and audit in this DbContext.
        var freshUser = await db.Users.SingleOrDefaultAsync(u => u.Id == createResult.UserId, ct);
        if (freshUser is null)
        {
            // Very unlikely — identity creation "succeeded" but the user is not visible. Bail safely.
            logger.LogError(
                "LinkOrCreate: stub creation reported success for user {UserId} but user not found in DB.",
                createResult.UserId);
            return new LinkOrCreateResult(LinkOrCreateOutcome.ConflictEmailUsedByAnotherPerson, null,
                "Účet byl vytvořen, ale nepodařilo se ho dohledat pro propojení.");
        }

        freshUser.PersonId = personId;

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(ApplicationUser),
            EntityId = freshUser.Id,
            Action = "CreateAccount",
            ActorUserId = actorUserId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                PersonId = personId,
                PersonName = FullName(person.FirstName, person.LastName),
                Method = "LinkOrCreate stub"
            })
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "LinkOrCreate: created stub user {UserId} for person {PersonId} (actor {ActorId}).",
            freshUser.Id, personId, actorUserId);

        return new LinkOrCreateResult(LinkOrCreateOutcome.Created, freshUser.Id, "Účet vytvořen.");
    }

    public async Task<BulkLinkOrCreateResult> LinkOrCreateAccountsForGameAsync(
        int gameId,
        string actorUserId,
        CancellationToken ct)
    {
        // Snapshot the target Person IDs up front so iteration is deterministic and not affected
        // by the per-iteration DbContexts opened by LinkOrCreateAccountAsync.
        List<int> targetPersonIds;
        await using (var db = await dbContextFactory.CreateDbContextAsync(ct))
        {
            // All adults in the game whose Person has a non-empty email and whose Person is not
            // yet linked to any ApplicationUser. Soft-deleted persons are filtered by the query
            // filter on Person.
            var linkedPersonIds = await db.Users.AsNoTracking()
                .Where(u => u.PersonId != null)
                .Select(u => u.PersonId!.Value)
                .ToListAsync(ct);
            var linkedSet = new HashSet<int>(linkedPersonIds);

            targetPersonIds = await db.Registrations.AsNoTracking()
                .Where(r => r.Submission.GameId == gameId
                    && r.AttendeeType == AttendeeType.Adult
                    && r.Status == RegistrationStatus.Active
                    && !r.Submission.IsDeleted
                    && !string.IsNullOrWhiteSpace(r.Person.Email))
                .Select(r => r.Person.Id)
                .Distinct()
                .ToListAsync(ct);

            targetPersonIds = targetPersonIds.Where(id => !linkedSet.Contains(id)).ToList();
        }

        var linked = 0;
        var created = 0;
        var missingEmail = 0;
        var alreadyLinked = 0;
        var conflicts = 0;
        var errors = new List<string>();

        foreach (var personId in targetPersonIds)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var result = await LinkOrCreateAccountAsync(personId, actorUserId, ct);
                switch (result.Outcome)
                {
                    case LinkOrCreateOutcome.Linked: linked++; break;
                    case LinkOrCreateOutcome.Created: created++; break;
                    case LinkOrCreateOutcome.MissingEmail: missingEmail++; break;
                    case LinkOrCreateOutcome.AlreadyLinked: alreadyLinked++; break;
                    case LinkOrCreateOutcome.ConflictEmailUsedByAnotherPerson:
                        conflicts++;
                        if (errors.Count < 3 && !string.IsNullOrWhiteSpace(result.Message))
                            errors.Add($"Osoba #{personId}: {result.Message}");
                        break;
                }
            }
            catch (Exception ex)
            {
                conflicts++;
                if (errors.Count < 3)
                    errors.Add($"Osoba #{personId}: {ex.Message}");
                logger.LogError(ex,
                    "LinkOrCreateAccountsForGame: unexpected error for person {PersonId} (actor {ActorId}).",
                    personId, actorUserId);
            }
        }

        logger.LogInformation(
            "LinkOrCreateAccountsForGame: game {GameId} — linked={Linked} created={Created} missingEmail={MissingEmail} alreadyLinked={AlreadyLinked} conflicts={Conflicts} (actor {ActorId}).",
            gameId, linked, created, missingEmail, alreadyLinked, conflicts, actorUserId);

        return new BulkLinkOrCreateResult(linked, created, missingEmail, conflicts, alreadyLinked, errors);
    }

    private static string FullName(string first, string last)
    {
        first = (first ?? "").Trim();
        last = (last ?? "").Trim();
        return string.Join(" ", new[] { first, last }.Where(s => s.Length > 0));
    }
}

/// <summary>
/// Production implementation of <see cref="IStubAccountCreator"/> — creates ApplicationUser via
/// <see cref="UserManager{TUser}"/>, assigns a strong random password, and adds the Registrant role
/// so the user can finish onboarding by simply signing in with their email (magic link, etc.).
/// </summary>
public sealed class IdentityStubAccountCreator(
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider,
    ILogger<IdentityStubAccountCreator> logger)
    : IStubAccountCreator
{
    public async Task<StubAccountCreateResult> CreateStubAsync(
        string email,
        string displayName,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = false,
            DisplayName = displayName,
            IsActive = true,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };

        var password = GenerateRandomPassword();
        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            var message = string.Join("; ", createResult.Errors.Select(e => e.Description));
            logger.LogWarning(
                "IdentityStubAccountCreator: CreateAsync failed for email {Email}: {Error}",
                email, message);
            return new StubAccountCreateResult(false, null, message);
        }

        // Registrant role mirrors what the magic-link auto-creation path does, so future sign-ins
        // get the expected claims. Missing role is non-fatal — log it and carry on.
        var addRoleResult = await userManager.AddToRoleAsync(user, RoleNames.Registrant);
        if (!addRoleResult.Succeeded)
        {
            logger.LogWarning(
                "IdentityStubAccountCreator: failed to add Registrant role to user {UserId}: {Error}",
                user.Id, string.Join("; ", addRoleResult.Errors.Select(e => e.Description)));
        }

        return new StubAccountCreateResult(true, user.Id, null);
    }

    private static string GenerateRandomPassword()
    {
        // 32 random bytes → ~43 Base64Url chars. Add a fixed suffix to guarantee Identity's
        // default complexity rules are met (lower + upper + digit + symbol) without rejecting
        // the "no matching class" error. The user never uses this password — they sign in via
        // the email flow — so we just need to pass Identity's validator.
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlTextEncoder.Encode(bytes) + "Aa1!";
    }
}
