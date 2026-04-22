using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Roles;

/// <summary>
/// View-model assembly for the /organizace/role (GameRoles.razor) page.
/// Extracted from the razor file so the logic is unit-testable against an InMemory DbContext.
/// </summary>
public sealed class GameRolesViewService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ILogger<GameRolesViewService>? logger = null)
{
    /// <summary>
    /// Loads all adult registrations for a game along with their current account-linking
    /// status (HasAccount / UserId) and assigned game-roles.
    ///
    /// HasAccount and the role lookup both honour TWO channels:
    ///   (a) email match — Person.Email == ApplicationUser.NormalizedEmail (legacy path);
    ///   (b) PersonId link — ApplicationUser.PersonId == Person.Id (populated by
    ///       AccountLinkingService and used when Person.Email is null or does not match).
    /// </summary>
    public async Task<List<AdultRoleView>> BuildAdultViewsAsync(int gameId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var adultRows = await db.Registrations.AsNoTracking()
            .Where(r => r.Submission.GameId == gameId
                && r.AttendeeType == AttendeeType.Adult
                && r.Status == RegistrationStatus.Active
                && !r.Submission.IsDeleted)
            .OrderBy(r => r.Person.LastName)
            .ThenBy(r => r.Person.FirstName)
            .Select(r => new
            {
                r.Id,
                PersonId = r.Person.Id,
                FirstName = r.Person.FirstName,
                LastName = r.Person.LastName,
                r.Submission.GroupName,
                Email = r.Person.Email,
                r.AdultRoles
            })
            .ToListAsync(ct);

        // Channel (a): emails-with-accounts for the adults on this page.
        var allEmails = adultRows
            .Where(a => !string.IsNullOrWhiteSpace(a.Email))
            .Select(a => a.Email!.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        var usersByNormalizedEmail = allEmails.Count > 0
            ? await db.Users.AsNoTracking()
                .Where(u => allEmails.Contains(u.NormalizedEmail!))
                .Select(u => new { u.Id, u.NormalizedEmail })
                .ToListAsync(ct)
            : [];

        var userIdByEmail = usersByNormalizedEmail
            .Where(u => u.NormalizedEmail is not null)
            .ToDictionary(u => u.NormalizedEmail!, u => u.Id, StringComparer.OrdinalIgnoreCase);

        // Channel (b): PersonId links for the adults on this page.
        var adultPersonIds = adultRows.Select(a => a.PersonId).Distinct().ToList();

        var personIdLinks = adultPersonIds.Count > 0
            ? await db.Users.AsNoTracking()
                .Where(u => u.PersonId != null && adultPersonIds.Contains(u.PersonId!.Value))
                .Select(u => new { u.Id, PersonId = u.PersonId!.Value })
                .ToListAsync(ct)
            : [];

        // Data-quality guard: if multiple ApplicationUsers are linked to the same Person (which
        // v0.9.19's AccountLinkingService was not defensive enough to prevent), picking the
        // naive ToDictionary(PersonId) throws ArgumentException and takes the whole page down.
        // Deterministically pick the earliest user Id and log the conflict so an admin can fix it.
        var duplicatePersonIds = personIdLinks
            .GroupBy(l => l.PersonId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicatePersonIds.Count > 0)
        {
            logger?.LogWarning(
                "Multiple ApplicationUsers linked to the same PersonId(s): {PersonIds}. Using the earliest user per person.",
                string.Join(",", duplicatePersonIds));
        }

        var userIdByPersonId = personIdLinks
            .GroupBy(l => l.PersonId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id, StringComparer.Ordinal).First().Id);

        // Load assigned game roles for this game keyed by UserId (not by email string),
        // so adults linked only via PersonId still see their roles.
        var assignedRoles = await db.GameRoles.AsNoTracking()
            .Where(gr => gr.GameId == gameId)
            .Select(gr => new { gr.UserId, gr.RoleName })
            .ToListAsync(ct);

        var rolesByUserId = assignedRoles
            .GroupBy(r => r.UserId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => r.RoleName).OrderBy(r => r, StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        return adultRows.Select(a =>
        {
            var email = a.Email?.Trim();
            var normalizedEmail = email?.ToUpperInvariant();

            // Resolve UserId via email first (common case), then fall back to PersonId link.
            string? userId = null;
            if (normalizedEmail is not null && userIdByEmail.TryGetValue(normalizedEmail, out var emailUserId))
                userId = emailUserId;
            else if (userIdByPersonId.TryGetValue(a.PersonId, out var personUserId))
                userId = personUserId;

            var assignedRolesList = userId is not null && rolesByUserId.TryGetValue(userId, out var roles)
                ? roles
                : new List<string>();

            return new AdultRoleView
            {
                RegistrationId = a.Id,
                PersonId = a.PersonId,
                FullName = $"{a.FirstName} {a.LastName}",
                LastName = a.LastName,
                FirstName = a.FirstName,
                GroupName = a.GroupName,
                Email = email,
                AdultRoles = a.AdultRoles,
                HasAccount = userId is not null,
                UserId = userId,
                AssignedGameRoles = assignedRolesList,
                SelectedRole = ""
            };
        }).ToList();
    }

    /// <summary>
    /// Groups adults by their officially <b>assigned</b> GameRole (operational view).
    /// An adult with multiple assigned roles appears in each matching group.
    /// Adults with no assigned GameRole land in the "Nezvoleno" catch-all.
    ///
    /// Group labels come from whatever role names exist in the <c>GameRoles</c> / <c>Roles</c>
    /// tables for this game — no hardcoded mapping to the 5 AdultRoleFlags labels.
    /// Each group is sorted by LastName, then FirstName (case-insensitive).
    /// Empty groups are dropped, unless they are in <paramref name="availableRoleNames"/>
    /// (in which case they are shown empty so organizers know the role exists but nobody
    /// is assigned yet).
    /// </summary>
    public static List<RoleGroup> GroupAdultsByRole(
        IReadOnlyList<AdultRoleView> adults,
        IReadOnlyList<string>? availableRoleNames = null)
    {
        // Union of role names that appear in assignments + any role names the caller wants
        // shown even when empty (so admins see all defined roles in the game).
        var assignedRoleNames = adults
            .SelectMany(a => a.AssignedGameRoles)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var allRoleNames = assignedRoleNames;
        if (availableRoleNames is not null)
        {
            allRoleNames = allRoleNames.Concat(availableRoleNames);
        }

        var orderedRoleNames = allRoleNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var result = new List<RoleGroup>(orderedRoleNames.Count + 1);

        foreach (var roleName in orderedRoleNames)
        {
            var inGroup = adults
                .Where(a => a.AssignedGameRoles.Contains(roleName, StringComparer.OrdinalIgnoreCase))
                .OrderBy(a => a.LastName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(a => a.FirstName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            // Always emit groups for known available roles; otherwise drop empty groups.
            var shouldEmit = inGroup.Count > 0
                || (availableRoleNames is not null
                    && availableRoleNames.Contains(roleName, StringComparer.OrdinalIgnoreCase));

            if (shouldEmit)
                result.Add(new RoleGroup(roleName, inGroup));
        }

        // "Nezvoleno" catch-all: adults with no assigned GameRole.
        var uncategorized = adults
            .Where(a => a.AssignedGameRoles.Count == 0)
            .OrderBy(a => a.LastName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.FirstName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (uncategorized.Count > 0)
            result.Add(new RoleGroup("Nezvoleno", uncategorized));

        return result;
    }
}

public sealed class AdultRoleView
{
    public int RegistrationId { get; set; }
    public int PersonId { get; set; }
    public string FullName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string? Email { get; set; }
    public AdultRoleFlags AdultRoles { get; set; }
    public bool HasAccount { get; set; }

    /// <summary>ApplicationUser.Id resolved via email-match OR PersonId link. Null when no account.</summary>
    public string? UserId { get; set; }

    public List<string> AssignedGameRoles { get; set; } = [];
    public string SelectedRole { get; set; } = "";
}

public sealed record RoleGroup(string RoleName, List<AdultRoleView> Adults);
