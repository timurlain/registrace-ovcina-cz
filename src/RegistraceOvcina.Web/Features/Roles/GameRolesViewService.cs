using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Roles;

/// <summary>
/// View-model assembly for the /organizace/role (GameRoles.razor) page.
/// Extracted from the razor file so the logic is unit-testable against an InMemory DbContext.
/// </summary>
public sealed class GameRolesViewService(IDbContextFactory<ApplicationDbContext> dbFactory)
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

        var userIdByPersonId = personIdLinks.ToDictionary(l => l.PersonId, l => l.Id);

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
    /// Groups adults by <see cref="AdultRoleFlags"/> using the 5 canonical labels
    /// (Příšera, Pomocník, Technická pomoc, Hraničář, Přihlížející).
    ///
    /// For each of the 5 roles, an adult appears in the group if EITHER:
    ///   - their AdultRoles flags include the matching flag (self-declared), OR
    ///   - they have an officially assigned matching GameRole (mapped to the same label).
    ///
    /// An adult with multiple flags appears in multiple groups.
    /// Adults with neither a flag nor an assignment land in a 6th "Nezvoleno" group.
    /// Each group is sorted by LastName, then FirstName (case-insensitive).
    /// Empty groups are dropped.
    /// </summary>
    public static List<RoleGroup> GroupAdultsByRole(IReadOnlyList<AdultRoleView> adults)
    {
        var result = new List<RoleGroup>(6);

        foreach (var (label, flag, assignedRoleNames) in RoleMappings)
        {
            var inGroup = adults
                .Where(a => a.AdultRoles.HasFlag(flag)
                    || a.AssignedGameRoles.Any(r => assignedRoleNames.Contains(r, StringComparer.OrdinalIgnoreCase)))
                .OrderBy(a => a.LastName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(a => a.FirstName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (inGroup.Count > 0)
                result.Add(new RoleGroup(label, inGroup));
        }

        // Catch-all bucket: no self-declared flag AND no assigned game role.
        var uncategorized = adults
            .Where(a => a.AdultRoles == AdultRoleFlags.None && a.AssignedGameRoles.Count == 0)
            .OrderBy(a => a.LastName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.FirstName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (uncategorized.Count > 0)
            result.Add(new RoleGroup("Nezvoleno", uncategorized));

        return result;
    }

    /// <summary>
    /// Canonical Czech label ↔ AdultRoleFlags ↔ assigned GameRole.RoleName mapping.
    /// The role-name list is intentionally loose because the admin can define arbitrary
    /// game roles in the Roles table; we use the common seeded names ("npc", "tech-support",
    /// "ranger-leader") plus a localized fallback so admins who use Czech role names still match.
    /// </summary>
    private static readonly (string Label, AdultRoleFlags Flag, string[] AssignedRoleNames)[] RoleMappings =
    [
        ("Příšera",         AdultRoleFlags.PlayMonster,        ["npc", "monster", "příšera"]),
        ("Pomocník",        AdultRoleFlags.OrganizationHelper, ["helper", "pomocník", "pomocnik"]),
        ("Technická pomoc", AdultRoleFlags.TechSupport,        ["tech-support", "tech", "technická pomoc"]),
        ("Hraničář",        AdultRoleFlags.RangerLeader,       ["ranger-leader", "hraničář", "hranicar"]),
        ("Přihlížející",    AdultRoleFlags.Spectator,          ["spectator", "přihlížející", "prihlizejici"]),
    ];
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
