using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Roles;

public sealed class GameRoleService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public async Task<List<string>> GetRolesForUserAsync(string email, int gameId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var normalizedEmail = email.Trim().ToUpperInvariant();

        var userId = await db.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail == normalizedEmail)
            .Select(u => u.Id)
            .FirstOrDefaultAsync();

        userId ??= await db.UserEmails.AsNoTracking()
            .Where(ue => ue.NormalizedEmail == normalizedEmail)
            .Select(ue => ue.UserId)
            .FirstOrDefaultAsync();

        if (userId is null) return [];

        return await db.GameRoles
            .AsNoTracking()
            .Where(gr => gr.UserId == userId && gr.GameId == gameId)
            .Select(gr => gr.RoleName)
            .ToListAsync();
    }

    public async Task<bool> HasRoleAsync(string email, int gameId, string roleName)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var normalizedRole = roleName.Trim().ToLowerInvariant();

        var userId = await db.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail == normalizedEmail)
            .Select(u => u.Id)
            .FirstOrDefaultAsync();

        userId ??= await db.UserEmails.AsNoTracking()
            .Where(ue => ue.NormalizedEmail == normalizedEmail)
            .Select(ue => ue.UserId)
            .FirstOrDefaultAsync();

        if (userId is null) return false;

        return await db.GameRoles
            .AsNoTracking()
            .AnyAsync(gr =>
                gr.UserId == userId &&
                gr.GameId == gameId &&
                gr.RoleName == normalizedRole);
    }

    public async Task AssignRoleAsync(string email, int gameId, string roleName, string actorUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var normalizedRole = roleName.Trim().ToLowerInvariant();

        var user = await db.Users
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);

        if (user is null)
            throw new InvalidOperationException($"Uživatel s e-mailem '{email}' nebyl nalezen.");

        var exists = await db.GameRoles
            .AnyAsync(gr => gr.UserId == user.Id && gr.GameId == gameId && gr.RoleName == normalizedRole);

        if (exists)
            return;

        db.GameRoles.Add(new GameRole
        {
            UserId = user.Id,
            GameId = gameId,
            RoleName = normalizedRole,
            AssignedAtUtc = DateTime.UtcNow
        });

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = "GameRole",
            EntityId = $"{user.Id}:{gameId}:{normalizedRole}",
            Action = "Assigned",
            ActorUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow,
            DetailsJson = JsonSerializer.Serialize(new { email, gameId, roleName = normalizedRole })
        });

        await db.SaveChangesAsync();
    }

    public async Task RevokeRoleAsync(string email, int gameId, string roleName, string actorUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var normalizedRole = roleName.Trim().ToLowerInvariant();

        var user = await db.Users
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);

        if (user is null)
            return;

        var gameRole = await db.GameRoles
            .FirstOrDefaultAsync(gr => gr.UserId == user.Id && gr.GameId == gameId && gr.RoleName == normalizedRole);

        if (gameRole is null)
            return;

        db.GameRoles.Remove(gameRole);

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = "GameRole",
            EntityId = $"{user.Id}:{gameId}:{normalizedRole}",
            Action = "Revoked",
            ActorUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow,
            DetailsJson = JsonSerializer.Serialize(new { email, gameId, roleName = normalizedRole })
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Assigns a role directly by ApplicationUser.Id, bypassing the email lookup.
    /// Required for accounts linked via ApplicationUser.PersonId where Person.Email is null
    /// or doesn't match ApplicationUser.NormalizedEmail.
    /// </summary>
    public async Task AssignRoleByUserIdAsync(string userId, int gameId, string roleName, string actorUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var normalizedRole = roleName.Trim().ToLowerInvariant();

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null)
            throw new InvalidOperationException($"Uživatel s ID '{userId}' nebyl nalezen.");

        var exists = await db.GameRoles
            .AnyAsync(gr => gr.UserId == userId && gr.GameId == gameId && gr.RoleName == normalizedRole);

        if (exists)
            return;

        db.GameRoles.Add(new GameRole
        {
            UserId = userId,
            GameId = gameId,
            RoleName = normalizedRole,
            AssignedAtUtc = DateTime.UtcNow
        });

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = "GameRole",
            EntityId = $"{userId}:{gameId}:{normalizedRole}",
            Action = "Assigned",
            ActorUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow,
            DetailsJson = JsonSerializer.Serialize(new { userId, gameId, roleName = normalizedRole })
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Revokes a role directly by ApplicationUser.Id (counterpart to <see cref="AssignRoleByUserIdAsync"/>).
    /// </summary>
    public async Task RevokeRoleByUserIdAsync(string userId, int gameId, string roleName, string actorUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var normalizedRole = roleName.Trim().ToLowerInvariant();

        var gameRole = await db.GameRoles
            .FirstOrDefaultAsync(gr => gr.UserId == userId && gr.GameId == gameId && gr.RoleName == normalizedRole);

        if (gameRole is null)
            return;

        db.GameRoles.Remove(gameRole);

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = "GameRole",
            EntityId = $"{userId}:{gameId}:{normalizedRole}",
            Action = "Revoked",
            ActorUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow,
            DetailsJson = JsonSerializer.Serialize(new { userId, gameId, roleName = normalizedRole })
        });

        await db.SaveChangesAsync();
    }

    public async Task<List<UserWithRoles>> GetAllRolesForGameAsync(int gameId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var roles = await db.GameRoles
            .AsNoTracking()
            .Where(gr => gr.GameId == gameId)
            .Select(gr => new
            {
                gr.User.Email,
                gr.User.DisplayName,
                gr.RoleName
            })
            .ToListAsync();

        return roles
            .GroupBy(r => new { r.Email, r.DisplayName })
            .Select(g => new UserWithRoles(
                g.Key.Email ?? "",
                g.Key.DisplayName ?? "",
                g.Select(r => r.RoleName).OrderBy(r => r).ToList()))
            .OrderBy(u => u.Email)
            .ToList();
    }

    public async Task<int> SeedRolesFromRegistrationsAsync(int gameId, string actorUserId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var registrations = await db.Registrations
            .AsNoTracking()
            .Where(r =>
                r.Submission.GameId == gameId &&
                r.Submission.Status == SubmissionStatus.Submitted &&
                r.Status == RegistrationStatus.Active &&
                !r.Submission.IsDeleted)
            .Select(r => new
            {
                r.Person.Email,
                r.AttendeeType,
                r.AdultRoles
            })
            .ToListAsync();

        var count = 0;

        foreach (var reg in registrations)
        {
            if (string.IsNullOrWhiteSpace(reg.Email))
                continue;

            var normalizedEmail = reg.Email.Trim().ToUpperInvariant();
            var user = await db.Users
                .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);

            if (user is null)
                continue;

            var rolesToAssign = new List<string>();

            if (reg.AttendeeType == AttendeeType.Player)
                rolesToAssign.Add("player");

            if (reg.AdultRoles.HasFlag(AdultRoleFlags.PlayMonster))
                rolesToAssign.Add("npc");

            if (reg.AdultRoles.HasFlag(AdultRoleFlags.OrganizationHelper) && !rolesToAssign.Contains("npc"))
                rolesToAssign.Add("npc");

            if (reg.AdultRoles.HasFlag(AdultRoleFlags.TechSupport))
                rolesToAssign.Add("tech-support");

            if (reg.AdultRoles.HasFlag(AdultRoleFlags.RangerLeader))
                rolesToAssign.Add("ranger-leader");

            foreach (var role in rolesToAssign)
            {
                var exists = await db.GameRoles
                    .AnyAsync(gr => gr.UserId == user.Id && gr.GameId == gameId && gr.RoleName == role);

                if (exists)
                    continue;

                db.GameRoles.Add(new GameRole
                {
                    UserId = user.Id,
                    GameId = gameId,
                    RoleName = role,
                    AssignedAtUtc = DateTime.UtcNow
                });

                count++;
            }
        }

        if (count > 0)
        {
            db.AuditLogs.Add(new AuditLog
            {
                EntityType = "GameRole",
                EntityId = gameId.ToString(),
                Action = "SeededFromRegistrations",
                ActorUserId = actorUserId,
                CreatedAtUtc = DateTime.UtcNow,
                DetailsJson = JsonSerializer.Serialize(new { gameId, rolesCreated = count })
            });

            await db.SaveChangesAsync();
        }

        return count;
    }

    public async Task<List<AttendeeRoleRow>> GetRegisteredAttendeesForGameAsync(int gameId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        return await db.Registrations
            .AsNoTracking()
            .Where(r => r.Submission.GameId == gameId && r.Status == RegistrationStatus.Active && !r.Submission.IsDeleted)
            .Select(r => new AttendeeRoleRow
            {
                PersonId = r.PersonId,
                PersonName = r.Person.FirstName + " " + r.Person.LastName,
                Email = r.Person.Email,
                IsPlayer = r.AttendeeType == AttendeeType.Player
            })
            .OrderBy(x => x.IsPlayer)  // Adults first
            .ThenBy(x => x.PersonName)
            .ToListAsync();
    }
}

public sealed record UserWithRoles(string Email, string DisplayName, List<string> Roles);

public sealed class AttendeeRoleRow
{
    public int PersonId { get; set; }
    public string PersonName { get; set; } = "";
    public string? Email { get; set; }
    public bool IsPlayer { get; set; }
    public List<string> CurrentRoles { get; set; } = [];
    public string SelectedRole { get; set; } = "";
}
