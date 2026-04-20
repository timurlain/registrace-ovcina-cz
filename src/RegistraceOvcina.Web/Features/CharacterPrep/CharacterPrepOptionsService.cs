using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.CharacterPrep;

/// <summary>
/// Read/write surface for <see cref="StartingEquipmentOption"/> rows that
/// back the Character-Prep card picker. Used by the organizer admin page at
/// <c>/organizace/hry/{gameId}/priprava-postav/vybava</c>.
/// </summary>
/// <remarks>
/// <para><c>Key</c> is the stable identifier and is normalized (trimmed +
/// lower-cased) on create. <see cref="UpdateAsync"/> intentionally does not
/// change it — parents' card selections reference the option by ID, but the
/// key is used for copy-across-games de-duplication and should stay valid once
/// chosen.</para>
/// <para><see cref="TryDeleteAsync"/> is reference-guarded: an option that is
/// attached to any <see cref="Registration"/> cannot be removed. The DB-side
/// FK is <c>DeleteBehavior.Restrict</c>, so attempting to delete such a row
/// would fail at SaveChanges anyway — we surface it as a user-friendly
/// <c>false</c> return here rather than a thrown exception.</para>
/// </remarks>
public sealed record StartingEquipmentOptionDto(
    int Id,
    int GameId,
    string Key,
    string DisplayName,
    string? Description,
    int SortOrder);

public sealed class CharacterPrepOptionsService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ILogger<CharacterPrepOptionsService> logger)
{
    private const int KeyMaxLength = 50;
    private const int DisplayNameMaxLength = 100;

    /// <summary>
    /// Lists every option for <paramref name="gameId"/> ordered by SortOrder,
    /// with DisplayName as a tiebreaker so the picker is deterministic when
    /// operators leave SortOrder at its default (0).
    /// </summary>
    public async Task<IReadOnlyList<StartingEquipmentOptionDto>> ListAsync(
        int gameId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        return await db.StartingEquipmentOptions
            .AsNoTracking()
            .Where(x => x.GameId == gameId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .Select(x => new StartingEquipmentOptionDto(
                x.Id, x.GameId, x.Key, x.DisplayName, x.Description, x.SortOrder))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a new option for <paramref name="gameId"/>. Throws
    /// <see cref="ArgumentException"/> for empty or over-long input,
    /// <see cref="InvalidOperationException"/> if the normalized key already
    /// exists on that game.
    /// </summary>
    public async Task<StartingEquipmentOptionDto> CreateAsync(
        int gameId,
        string key,
        string displayName,
        string? description,
        int sortOrder,
        CancellationToken cancellationToken)
    {
        var normalizedKey = NormalizeKey(key);
        var trimmedDisplayName = (displayName ?? string.Empty).Trim();
        var trimmedDescription = NormalizeOptional(description);

        if (string.IsNullOrEmpty(normalizedKey))
        {
            throw new ArgumentException("Key is required.", nameof(key));
        }
        if (normalizedKey.Length > KeyMaxLength)
        {
            throw new ArgumentException(
                $"Key must be {KeyMaxLength} characters or fewer.", nameof(key));
        }
        if (string.IsNullOrEmpty(trimmedDisplayName))
        {
            throw new ArgumentException("DisplayName is required.", nameof(displayName));
        }
        if (trimmedDisplayName.Length > DisplayNameMaxLength)
        {
            throw new ArgumentException(
                $"DisplayName must be {DisplayNameMaxLength} characters or fewer.",
                nameof(displayName));
        }
        if (sortOrder < 0)
        {
            throw new ArgumentException("SortOrder must be non-negative.", nameof(sortOrder));
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var duplicate = await db.StartingEquipmentOptions
            .AsNoTracking()
            .AnyAsync(x => x.GameId == gameId && x.Key == normalizedKey, cancellationToken);
        if (duplicate)
        {
            throw new InvalidOperationException(
                $"Starting equipment option with key '{normalizedKey}' already exists for game {gameId}.");
        }

        var entity = new StartingEquipmentOption
        {
            GameId = gameId,
            Key = normalizedKey,
            DisplayName = trimmedDisplayName,
            Description = trimmedDescription,
            SortOrder = sortOrder
        };
        db.StartingEquipmentOptions.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Created StartingEquipmentOption {OptionId} (Key='{Key}') for game {GameId}.",
            entity.Id, entity.Key, gameId);

        return new StartingEquipmentOptionDto(
            entity.Id, entity.GameId, entity.Key, entity.DisplayName, entity.Description, entity.SortOrder);
    }

    /// <summary>
    /// Updates the display metadata and sort order of an existing option.
    /// Key is intentionally immutable — parents' submissions reference the
    /// option by Id, but the key is the stable cross-game identifier used by
    /// <see cref="CopyFromGameAsync"/>.
    /// </summary>
    public async Task UpdateAsync(
        int optionId,
        string displayName,
        string? description,
        int sortOrder,
        CancellationToken cancellationToken)
    {
        var trimmedDisplayName = (displayName ?? string.Empty).Trim();
        var trimmedDescription = NormalizeOptional(description);

        if (string.IsNullOrEmpty(trimmedDisplayName))
        {
            throw new ArgumentException("DisplayName is required.", nameof(displayName));
        }
        if (trimmedDisplayName.Length > DisplayNameMaxLength)
        {
            throw new ArgumentException(
                $"DisplayName must be {DisplayNameMaxLength} characters or fewer.",
                nameof(displayName));
        }
        if (sortOrder < 0)
        {
            throw new ArgumentException("SortOrder must be non-negative.", nameof(sortOrder));
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.StartingEquipmentOptions
            .FirstOrDefaultAsync(x => x.Id == optionId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"StartingEquipmentOption {optionId} not found.");

        entity.DisplayName = trimmedDisplayName;
        entity.Description = trimmedDescription;
        entity.SortOrder = sortOrder;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Updated StartingEquipmentOption {OptionId} (Key='{Key}').", entity.Id, entity.Key);
    }

    /// <summary>
    /// Attempts to delete an option. Returns <c>false</c> (without deleting)
    /// if at least one <see cref="Registration"/> still references it, so the
    /// caller can surface a friendly message. Returns <c>true</c> after a
    /// successful delete.
    /// </summary>
    public async Task<bool> TryDeleteAsync(int optionId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.StartingEquipmentOptions
            .FirstOrDefaultAsync(x => x.Id == optionId, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        var referenced = await db.Registrations
            .AsNoTracking()
            .AnyAsync(r => r.StartingEquipmentOptionId == optionId, cancellationToken);
        if (referenced)
        {
            logger.LogInformation(
                "Refused to delete StartingEquipmentOption {OptionId}: still referenced by at least one Registration.",
                optionId);
            return false;
        }

        db.StartingEquipmentOptions.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Deleted StartingEquipmentOption {OptionId}.", optionId);
        return true;
    }

    /// <summary>
    /// Copies every option from <paramref name="sourceGameId"/> into
    /// <paramref name="targetGameId"/>, skipping any (target-side) key that
    /// already exists. Preserves DisplayName, Description and SortOrder.
    /// Use this to bootstrap a new game's options from a previous year.
    /// </summary>
    public async Task CopyFromGameAsync(
        int sourceGameId,
        int targetGameId,
        CancellationToken cancellationToken)
    {
        if (sourceGameId == targetGameId)
        {
            // No-op rather than a throw — the page UI already filters the
            // current game out of the source dropdown, but being defensive is
            // cheap.
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var sourceRows = await db.StartingEquipmentOptions
            .AsNoTracking()
            .Where(x => x.GameId == sourceGameId)
            .ToListAsync(cancellationToken);

        if (sourceRows.Count == 0)
        {
            return;
        }

        var existingTargetKeys = await db.StartingEquipmentOptions
            .AsNoTracking()
            .Where(x => x.GameId == targetGameId)
            .Select(x => x.Key)
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<string>(existingTargetKeys, StringComparer.Ordinal);

        var copied = 0;
        foreach (var row in sourceRows)
        {
            if (existingSet.Contains(row.Key))
            {
                continue;
            }
            db.StartingEquipmentOptions.Add(new StartingEquipmentOption
            {
                GameId = targetGameId,
                Key = row.Key,
                DisplayName = row.DisplayName,
                Description = row.Description,
                SortOrder = row.SortOrder
            });
            copied++;
        }

        if (copied > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Copied {Copied} StartingEquipmentOption rows from game {SourceId} to game {TargetId}.",
                copied, sourceGameId, targetGameId);
        }
    }

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
