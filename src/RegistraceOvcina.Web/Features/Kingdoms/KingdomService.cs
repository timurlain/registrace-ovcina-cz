using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Kingdoms;

public sealed class KingdomService(IDbContextFactory<ApplicationDbContext> dbContextFactory, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<Kingdom>> GetAllKingdomsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Kingdoms
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CreateKingdomAsync(string name, string displayName, string? color, string actorUserId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var exists = await db.Kingdoms.AnyAsync(x => x.Name == name.Trim(), cancellationToken);
        if (exists)
        {
            throw new ValidationException($"Království s názvem '{name.Trim()}' již existuje.");
        }

        var kingdom = new Kingdom
        {
            Name = name.Trim(),
            DisplayName = displayName.Trim(),
            Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim()
        };

        db.Kingdoms.Add(kingdom);
        await db.SaveChangesAsync(cancellationToken);

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(Kingdom),
            EntityId = kingdom.Id.ToString(),
            Action = "KingdomCreated",
            ActorUserId = actorUserId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                kingdom.Name,
                kingdom.DisplayName,
                kingdom.Color
            })
        });

        await db.SaveChangesAsync(cancellationToken);
        return kingdom.Id;
    }

    public async Task UpdateKingdomAsync(int id, string name, string displayName, string? color, string actorUserId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var kingdom = await db.Kingdoms.FindAsync([id], cancellationToken)
            ?? throw new ValidationException("Království nebylo nalezeno.");

        var duplicate = await db.Kingdoms.AnyAsync(x => x.Name == name.Trim() && x.Id != id, cancellationToken);
        if (duplicate)
        {
            throw new ValidationException($"Království s názvem '{name.Trim()}' již existuje.");
        }

        kingdom.Name = name.Trim();
        kingdom.DisplayName = displayName.Trim();
        kingdom.Color = string.IsNullOrWhiteSpace(color) ? null : color.Trim();

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(Kingdom),
            EntityId = kingdom.Id.ToString(),
            Action = "KingdomUpdated",
            ActorUserId = actorUserId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                kingdom.Name,
                kingdom.DisplayName,
                kingdom.Color
            })
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteKingdomAsync(int id, string actorUserId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var kingdom = await db.Kingdoms.FindAsync([id], cancellationToken)
            ?? throw new ValidationException("Království nebylo nalezeno.");

        var hasTargets = await db.GameKingdomTargets.AnyAsync(x => x.KingdomId == id, cancellationToken);
        if (hasTargets)
        {
            throw new ValidationException("Království nelze smazat, protože je přiřazeno k některé hře.");
        }

        var hasRegistrations = await db.Registrations.AnyAsync(x => x.PreferredKingdomId == id, cancellationToken);
        if (hasRegistrations)
        {
            throw new ValidationException("Království nelze smazat, protože má přiřazené registrace.");
        }

        var hasAppearances = await db.Set<CharacterAppearance>().AnyAsync(x => x.AssignedKingdomId == id, cancellationToken);
        if (hasAppearances)
        {
            throw new ValidationException("Království nelze smazat, protože má přiřazené postavy.");
        }

        db.Kingdoms.Remove(kingdom);

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(Kingdom),
            EntityId = kingdom.Id.ToString(),
            Action = "KingdomDeleted",
            ActorUserId = actorUserId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                kingdom.Name,
                kingdom.DisplayName
            })
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GameKingdomTarget>> GetGameKingdomTargetsAsync(int gameId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.GameKingdomTargets
            .AsNoTracking()
            .Include(x => x.Kingdom)
            .Where(x => x.GameId == gameId)
            .OrderBy(x => x.Kingdom.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveGameKingdomTargetsAsync(int gameId, List<GameKingdomTargetInput> targets, string actorUserId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var game = await db.Games.FindAsync([gameId], cancellationToken)
            ?? throw new ValidationException("Hra nebyla nalezena.");

        var existing = await db.GameKingdomTargets
            .Where(x => x.GameId == gameId)
            .ToListAsync(cancellationToken);

        db.GameKingdomTargets.RemoveRange(existing);

        foreach (var target in targets)
        {
            db.GameKingdomTargets.Add(new GameKingdomTarget
            {
                GameId = gameId,
                KingdomId = target.KingdomId,
                TargetPlayerCount = target.TargetPlayerCount
            });
        }

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(GameKingdomTarget),
            EntityId = gameId.ToString(),
            Action = "GameKingdomTargetsSaved",
            ActorUserId = actorUserId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                GameId = gameId,
                Targets = targets
            })
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Game?> GetGameAsync(int gameId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == gameId, cancellationToken);
    }
}

public sealed record GameKingdomTargetInput(int KingdomId, int TargetPlayerCount);
