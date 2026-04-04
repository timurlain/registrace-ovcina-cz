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
