using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Security;

namespace RegistraceOvcina.Web.Features.Users;

public sealed class UserAdministrationService(IDbContextFactory<ApplicationDbContext> dbContextFactory, TimeProvider timeProvider)
{
    private static readonly string[] ManageableRoles = [RoleNames.Organizer, RoleNames.Admin];

    public async Task<UserAdministrationPage> GetPageAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var users = await db.Users
            .AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.Email)
            .ToListAsync(cancellationToken);

        var roleAssignments = await db.UserRoles
            .AsNoTracking()
            .Join(
                db.Roles.AsNoTracking(),
                userRole => userRole.RoleId,
                role => role.Id,
                (userRole, role) => new
                {
                    userRole.UserId,
                    RoleName = role.Name!
                })
            .Where(x => ManageableRoles.Contains(x.RoleName))
            .ToListAsync(cancellationToken);

        var rolesByUser = roleAssignments
            .GroupBy(x => x.UserId)
            .ToDictionary(
                x => x.Key,
                x => x.Select(y => y.RoleName).ToHashSet(StringComparer.Ordinal));

        var summaries = users
            .Select(user =>
            {
                var userRoles = rolesByUser.GetValueOrDefault(user.Id);
                var isAdmin = userRoles?.Contains(RoleNames.Admin) == true;
                var isOrganizer = userRoles?.Contains(RoleNames.Organizer) == true;

                return new ManagedUserSummary(
                    user.Id,
                    user.DisplayName,
                    user.Email ?? string.Empty,
                    user.IsActive,
                    isOrganizer,
                    isAdmin,
                    user.LastLoginAtUtc,
                    user.CreatedAtUtc,
                    false);
            })
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.IsAdmin)
            .ThenByDescending(x => x.IsOrganizer)
            .ThenBy(x => x.DisplayName, StringComparer.CurrentCulture)
            .ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var activeAdmins = summaries
            .Where(x => x.IsAdmin && x.IsActive)
            .Select(x => x.Id)
            .ToList();

        if (activeAdmins.Count == 1)
        {
            var protectedAdminId = activeAdmins[0];
            summaries = summaries
                .Select(x => x.Id == protectedAdminId ? x with { IsLastActiveAdmin = true } : x)
                .ToList();
        }

        return new UserAdministrationPage(
            summaries.Where(x => x.IsStaff).ToList(),
            summaries.Where(x => !x.IsStaff).ToList(),
            summaries.Count,
            summaries.Count(x => x.IsStaff && x.IsActive),
            summaries.Count(x => x.IsAdmin && x.IsActive));
    }

    public async Task<UserManagementChangeResult> ToggleRoleAsync(
        string userId,
        string roleName,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (!ManageableRoles.Contains(roleName, StringComparer.Ordinal))
        {
            throw new ValidationException("Tuto roli nelze na této stránce spravovat.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new ValidationException("Uživatel nebyl nalezen.");

        var role = await db.Roles.SingleOrDefaultAsync(x => x.Name == roleName, cancellationToken)
            ?? throw new ValidationException($"Role '{roleName}' nebyla nalezena.");

        var membership = await db.UserRoles.SingleOrDefaultAsync(
            x => x.UserId == userId && x.RoleId == role.Id,
            cancellationToken);

        var granted = membership is null;
        if (!granted && roleName == RoleNames.Admin)
        {
            await EnsureAnotherActiveAdminExistsAsync(db, user.Id, cancellationToken);
        }

        if (granted)
        {
            db.UserRoles.Add(new IdentityUserRole<string>
            {
                UserId = user.Id,
                RoleId = role.Id
            });
        }
        else
        {
            db.UserRoles.Remove(membership!);
        }

        user.SecurityStamp = Guid.NewGuid().ToString("N");

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(ApplicationUser),
            EntityId = user.Id,
            Action = granted ? "UserRoleGranted" : "UserRoleRevoked",
            ActorUserId = actorUserId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                user.Email,
                user.DisplayName,
                Role = roleName,
                Granted = granted
            })
        });

        await db.SaveChangesAsync(cancellationToken);

        return new UserManagementChangeResult(GetRoleStatusCode(roleName, granted));
    }

    public async Task<UserManagementChangeResult> ToggleActiveAsync(
        string userId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new ValidationException("Uživatel nebyl nalezen.");

        var activating = !user.IsActive;
        if (!activating)
        {
            await EnsureAnotherActiveAdminExistsAsync(db, user.Id, cancellationToken);
        }

        user.IsActive = activating;
        user.SecurityStamp = Guid.NewGuid().ToString("N");

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(ApplicationUser),
            EntityId = user.Id,
            Action = activating ? "UserActivated" : "UserDeactivated",
            ActorUserId = actorUserId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                user.Email,
                user.DisplayName,
                user.IsActive
            })
        });

        await db.SaveChangesAsync(cancellationToken);

        return new UserManagementChangeResult(activating ? "user-activated" : "user-deactivated");
    }

    private static async Task EnsureAnotherActiveAdminExistsAsync(
        ApplicationDbContext db,
        string userId,
        CancellationToken cancellationToken)
    {
        var targetIsActiveAdmin = await (
                from user in db.Users
                join userRole in db.UserRoles on user.Id equals userRole.UserId
                join role in db.Roles on userRole.RoleId equals role.Id
                where user.Id == userId && user.IsActive && role.Name == RoleNames.Admin
                select user.Id)
            .AnyAsync(cancellationToken);

        if (!targetIsActiveAdmin)
        {
            return;
        }

        var anotherActiveAdminExists = await (
                from user in db.Users
                join userRole in db.UserRoles on user.Id equals userRole.UserId
                join role in db.Roles on userRole.RoleId equals role.Id
                where user.Id != userId && user.IsActive && role.Name == RoleNames.Admin
                select user.Id)
            .AnyAsync(cancellationToken);

        if (!anotherActiveAdminExists)
        {
            throw new ValidationException("Posledního aktivního správce nelze odebrat ani deaktivovat.");
        }
    }

    private static string GetRoleStatusCode(string roleName, bool granted) =>
        roleName switch
        {
            RoleNames.Organizer => granted ? "organizer-added" : "organizer-removed",
            RoleNames.Admin => granted ? "admin-added" : "admin-removed",
            _ => throw new InvalidOperationException($"Unsupported role '{roleName}'.")
        };

    public async Task<UserLookupResult?> LookupByEmailAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var normalizedEmail = email.Trim().ToUpperInvariant();
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // Find user by primary or alternate email
        var userId = await db.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail == normalizedEmail)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);

        userId ??= await db.UserEmails.AsNoTracking()
            .Where(ue => ue.NormalizedEmail == normalizedEmail)
            .Select(ue => ue.UserId)
            .FirstOrDefaultAsync(ct);

        if (userId is null) return null;

        return await LookupByIdAsync(userId, ct);
    }

    public async Task<UserLookupResult?> LookupByIdAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.IsActive, u.LastLoginAtUtc, u.CreatedAtUtc })
            .SingleOrDefaultAsync(ct);

        if (user is null) return null;

        var identityRoles = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
            .ToListAsync(ct);

        var alternateEmails = await db.UserEmails.AsNoTracking()
            .Where(ue => ue.UserId == userId)
            .OrderBy(ue => ue.Email)
            .ToListAsync(ct);

        var gameRoles = await db.GameRoles.AsNoTracking()
            .Where(gr => gr.UserId == userId)
            .Join(db.Games, gr => gr.GameId, g => g.Id, (gr, g) => new GameRoleSummary(g.Id, g.Name, gr.RoleName))
            .ToListAsync(ct);

        return new UserLookupResult(
            user.Id, user.DisplayName, user.Email ?? "",
            user.IsActive, user.LastLoginAtUtc, user.CreatedAtUtc,
            identityRoles, alternateEmails, gameRoles);
    }

    public async Task<List<UserSearchResult>> SearchUsersAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return [];

        var term = query.Trim().ToUpperInvariant();
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // Find user IDs matching by primary email or display name
        var primaryMatches = await db.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail!.Contains(term) || u.DisplayName.ToUpper().Contains(term))
            .Select(u => u.Id)
            .Take(10)
            .ToListAsync(ct);

        // Find user IDs matching by alternate email
        var alternateMatches = await db.UserEmails.AsNoTracking()
            .Where(ue => ue.NormalizedEmail.Contains(term))
            .Select(ue => ue.UserId)
            .Distinct()
            .Take(10)
            .ToListAsync(ct);

        var allUserIds = primaryMatches.Union(alternateMatches).Distinct().ToList();
        if (allUserIds.Count == 0) return [];

        var results = await db.Users.AsNoTracking()
            .Where(u => allUserIds.Contains(u.Id))
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Email)
            .Take(10)
            .Select(u => new UserSearchResult(u.Id, u.DisplayName, u.Email ?? "", u.IsActive, u.LastLoginAtUtc))
            .ToListAsync(ct);

        return results;
    }

    public async Task RenameUserAsync(string userId, string newDisplayName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newDisplayName))
            throw new ValidationException("Jméno nesmí být prázdné.");

        newDisplayName = newDisplayName.Trim();
        if (newDisplayName.Length > 200)
            throw new ValidationException("Jméno je příliš dlouhé (max 200 znaků).");

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new ValidationException("Uživatel nebyl nalezen.");

        var oldName = user.DisplayName;
        user.DisplayName = newDisplayName;

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(ApplicationUser),
            EntityId = user.Id,
            Action = "UserRenamed",
            ActorUserId = userId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                OldDisplayName = oldName,
                NewDisplayName = newDisplayName,
                user.Email
            })
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task MergeUsersAsync(string canonicalUserId, string duplicateUserId, CancellationToken ct = default)
    {
        if (canonicalUserId == duplicateUserId)
            throw new ValidationException("Nelze sloučit uživatele sám se sebou.");

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var canonical = await db.Users.SingleOrDefaultAsync(u => u.Id == canonicalUserId, ct)
            ?? throw new ValidationException("Cílový uživatel nebyl nalezen.");
        var duplicate = await db.Users.SingleOrDefaultAsync(u => u.Id == duplicateUserId, ct)
            ?? throw new ValidationException("Duplicitní uživatel nebyl nalezen.");

        // 1. Transfer identity roles
        var canonicalRoleIds = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == canonicalUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var duplicateRoles = await db.UserRoles
            .Where(ur => ur.UserId == duplicateUserId)
            .ToListAsync(ct);

        foreach (var role in duplicateRoles)
        {
            if (!canonicalRoleIds.Contains(role.RoleId))
            {
                db.UserRoles.Add(new IdentityUserRole<string>
                {
                    UserId = canonicalUserId,
                    RoleId = role.RoleId
                });
            }
            db.UserRoles.Remove(role);
        }

        // 2. Transfer game roles
        var canonicalGameRoles = await db.GameRoles.AsNoTracking()
            .Where(gr => gr.UserId == canonicalUserId)
            .Select(gr => new { gr.GameId, gr.RoleName })
            .ToListAsync(ct);

        var duplicateGameRoles = await db.GameRoles
            .Where(gr => gr.UserId == duplicateUserId)
            .ToListAsync(ct);

        foreach (var gr in duplicateGameRoles)
        {
            if (!canonicalGameRoles.Any(c => c.GameId == gr.GameId && c.RoleName == gr.RoleName))
            {
                gr.UserId = canonicalUserId;
            }
            else
            {
                db.GameRoles.Remove(gr);
            }
        }

        // 3. Transfer external logins
        var duplicateLogins = await db.UserLogins
            .Where(ul => ul.UserId == duplicateUserId)
            .ToListAsync(ct);

        foreach (var login in duplicateLogins)
        {
            db.UserLogins.Remove(login);
            db.UserLogins.Add(new IdentityUserLogin<string>
            {
                UserId = canonicalUserId,
                LoginProvider = login.LoginProvider,
                ProviderKey = login.ProviderKey,
                ProviderDisplayName = login.ProviderDisplayName
            });
        }

        // 4. Transfer alternate emails
        var canonicalEmails = await db.UserEmails.AsNoTracking()
            .Where(ue => ue.UserId == canonicalUserId)
            .Select(ue => ue.NormalizedEmail)
            .ToListAsync(ct);

        var duplicateEmails = await db.UserEmails
            .Where(ue => ue.UserId == duplicateUserId)
            .ToListAsync(ct);

        foreach (var ue in duplicateEmails)
        {
            if (!canonicalEmails.Contains(ue.NormalizedEmail))
            {
                ue.UserId = canonicalUserId;
            }
            else
            {
                db.UserEmails.Remove(ue);
            }
        }

        // 5. Copy PersonId if canonical doesn't have one
        if (canonical.PersonId is null && duplicate.PersonId is not null)
        {
            canonical.PersonId = duplicate.PersonId;
        }

        // 6. Deactivate duplicate
        duplicate.IsActive = false;
        duplicate.SecurityStamp = Guid.NewGuid().ToString("N");

        // 7. Add duplicate's primary email as alternate on canonical (if different and not already there)
        var duplicatePrimaryNormalized = duplicate.Email?.Trim().ToUpperInvariant();
        var canonicalPrimaryNormalized = canonical.Email?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(duplicatePrimaryNormalized)
            && duplicatePrimaryNormalized != canonicalPrimaryNormalized
            && !canonicalEmails.Contains(duplicatePrimaryNormalized))
        {
            db.UserEmails.Add(new UserEmail
            {
                UserId = canonicalUserId,
                Email = duplicate.Email!.Trim(),
                NormalizedEmail = duplicatePrimaryNormalized,
                CreatedAtUtc = nowUtc
            });
        }

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(ApplicationUser),
            EntityId = canonicalUserId,
            Action = "UserMerged",
            ActorUserId = canonicalUserId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                CanonicalUserId = canonicalUserId,
                CanonicalEmail = canonical.Email,
                DuplicateUserId = duplicateUserId,
                DuplicateEmail = duplicate.Email,
                DuplicateDisplayName = duplicate.DisplayName
            })
        });

        await db.SaveChangesAsync(ct);
    }
}

public sealed record UserAdministrationPage(
    IReadOnlyList<ManagedUserSummary> StaffUsers,
    IReadOnlyList<ManagedUserSummary> OtherUsers,
    int TotalUsers,
    int ActiveStaffCount,
    int AdminCount);

public sealed record ManagedUserSummary(
    string Id,
    string DisplayName,
    string Email,
    bool IsActive,
    bool IsOrganizer,
    bool IsAdmin,
    DateTime? LastLoginAtUtc,
    DateTime CreatedAtUtc,
    bool IsLastActiveAdmin)
{
    public bool IsStaff => IsOrganizer || IsAdmin;

    public string DisplayLabel => string.IsNullOrWhiteSpace(DisplayName) ? Email : DisplayName;
}

public sealed record UserManagementChangeResult(string StatusCode);

public sealed record UserLookupResult(
    string Id, string DisplayName, string PrimaryEmail,
    bool IsActive, DateTime? LastLoginAtUtc, DateTime CreatedAtUtc,
    List<string> IdentityRoles, List<UserEmail> AlternateEmails,
    List<GameRoleSummary> GameRoles);

public sealed record GameRoleSummary(int GameId, string GameName, string RoleName);

public sealed record UserSearchResult(string Id, string DisplayName, string Email, bool IsActive, DateTime? LastLoginAtUtc);
