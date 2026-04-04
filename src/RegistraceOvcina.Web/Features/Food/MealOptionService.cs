using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Food;

public sealed class MealOptionService(IDbContextFactory<ApplicationDbContext> dbContextFactory, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<MealOption>> GetMealOptionsForGameAsync(int gameId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.MealOptions
            .AsNoTracking()
            .Where(x => x.GameId == gameId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CreateMealOptionAsync(int gameId, string name, decimal price, string actorUserId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var game = await db.Games.FindAsync([gameId], cancellationToken)
            ?? throw new ValidationException("Hra nebyla nalezena.");

        var mealOption = new MealOption
        {
            GameId = gameId,
            Name = name.Trim(),
            Price = price,
            IsActive = true
        };

        db.MealOptions.Add(mealOption);
        await db.SaveChangesAsync(cancellationToken);

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(MealOption),
            EntityId = mealOption.Id.ToString(),
            Action = "MealOptionCreated",
            ActorUserId = actorUserId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                mealOption.GameId,
                mealOption.Name,
                mealOption.Price
            })
        });

        await db.SaveChangesAsync(cancellationToken);
        return mealOption.Id;
    }

    public async Task UpdateMealOptionAsync(int id, string name, decimal price, bool isActive, string actorUserId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var mealOption = await db.MealOptions.FindAsync([id], cancellationToken)
            ?? throw new ValidationException("Jídlo nebylo nalezeno.");

        mealOption.Name = name.Trim();
        mealOption.Price = price;
        mealOption.IsActive = isActive;

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(MealOption),
            EntityId = mealOption.Id.ToString(),
            Action = "MealOptionUpdated",
            ActorUserId = actorUserId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                mealOption.Name,
                mealOption.Price,
                mealOption.IsActive
            })
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteMealOptionAsync(int id, string actorUserId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var mealOption = await db.MealOptions.FindAsync([id], cancellationToken)
            ?? throw new ValidationException("Jídlo nebylo nalezeno.");

        var hasOrders = await db.FoodOrders.AnyAsync(x => x.MealOptionId == id, cancellationToken);
        if (hasOrders)
        {
            throw new ValidationException("Jídlo nelze smazat, protože k němu existují objednávky.");
        }

        db.MealOptions.Remove(mealOption);

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(MealOption),
            EntityId = mealOption.Id.ToString(),
            Action = "MealOptionDeleted",
            ActorUserId = actorUserId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                mealOption.GameId,
                mealOption.Name,
                mealOption.Price
            })
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> GetGameNameAsync(int gameId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var game = await db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == gameId, cancellationToken);

        return game?.Name ?? "Hra";
    }
}
