using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.CharacterPrep;

public sealed class CharacterPrepService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<CharacterPrepService>? logger = null)
{
    public async Task<CharacterPrepView> GetPrepViewAsync(
        RegistrationSubmission submission,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var game = await db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == submission.GameId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Game {submission.GameId} for submission {submission.Id} not found.");

        var gameStart = new DateTimeOffset(
            DateTime.SpecifyKind(game.StartsAtUtc, DateTimeKind.Utc));

        var rows = await db.Registrations
            .AsNoTracking()
            .Where(x => x.SubmissionId == submission.Id && x.AttendeeType == AttendeeType.Player)
            .OrderBy(x => x.Person.LastName)
            .ThenBy(x => x.Person.FirstName)
            .Select(x => new CharacterPrepRow(
                x.Id,
                x.Person.FirstName + " " + x.Person.LastName,
                x.CharacterName,
                x.StartingEquipmentOptionId,
                x.CharacterPrepNote,
                x.CharacterPrepUpdatedAtUtc))
            .ToListAsync(cancellationToken);

        var options = await db.StartingEquipmentOptions
            .AsNoTracking()
            .Where(x => x.GameId == submission.GameId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .Select(x => new StartingEquipmentOptionView(
                x.Id,
                x.Key,
                x.DisplayName,
                x.Description,
                x.SortOrder))
            .ToListAsync(cancellationToken);

        var isReadOnly = gameStart <= nowUtc;

        return new CharacterPrepView(
            submission.Id,
            submission.GameId,
            game.Name,
            gameStart,
            isReadOnly,
            rows,
            options);
    }

    public async Task SaveAsync(
        int submissionId,
        IEnumerable<CharacterPrepSaveRow> rows,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        // Materialize once — the caller may pass a lazy IEnumerable and we iterate twice.
        var rowList = rows as IReadOnlyList<CharacterPrepSaveRow> ?? rows.ToList();

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var submission = await db.RegistrationSubmissions
            .FirstOrDefaultAsync(x => x.Id == submissionId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"RegistrationSubmission {submissionId} not found.");

        var registrations = await db.Registrations
            .Include(x => x.StartingEquipmentOption)
            .Where(x => x.SubmissionId == submissionId)
            .ToListAsync(cancellationToken);

        var byId = registrations.ToDictionary(x => x.Id);

        // Resolve all requested equipment options in one query so we can validate
        // they belong to the submission's game (defense against cross-game spoofing).
        var optionIds = rowList
            .Where(r => r.StartingEquipmentOptionId.HasValue)
            .Select(r => r.StartingEquipmentOptionId!.Value)
            .Distinct()
            .ToList();

        var optionGameLookup = optionIds.Count == 0
            ? new Dictionary<int, int>()
            : await db.StartingEquipmentOptions
                .AsNoTracking()
                .Where(x => optionIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.GameId, cancellationToken);

        foreach (var row in rowList)
        {
            if (!byId.TryGetValue(row.RegistrationId, out var registration))
            {
                logger?.LogDebug(
                    "Character prep save row for registration {RegistrationId} ignored: not part of submission {SubmissionId}.",
                    row.RegistrationId, submissionId);
                continue;
            }

            if (registration.SubmissionId != submissionId)
            {
                logger?.LogDebug(
                    "Character prep save row for registration {RegistrationId} ignored: submission mismatch.",
                    row.RegistrationId);
                continue;
            }

            if (registration.AttendeeType != AttendeeType.Player)
            {
                logger?.LogDebug(
                    "Character prep save row for registration {RegistrationId} ignored: not a Player.",
                    row.RegistrationId);
                continue;
            }

            if (row.StartingEquipmentOptionId.HasValue)
            {
                if (!optionGameLookup.TryGetValue(row.StartingEquipmentOptionId.Value, out var optionGameId))
                {
                    throw new ArgumentException(
                        $"Starting equipment option {row.StartingEquipmentOptionId.Value} does not exist.",
                        nameof(rows));
                }

                if (optionGameId != submission.GameId)
                {
                    throw new ArgumentException(
                        $"Starting equipment option {row.StartingEquipmentOptionId.Value} belongs to game {optionGameId}, but submission {submissionId} is for game {submission.GameId}.",
                        nameof(rows));
                }
            }

            var newCharacterName = NormalizeOptional(row.CharacterName);
            var newNote = NormalizeOptional(row.CharacterPrepNote);
            var newOptionId = row.StartingEquipmentOptionId;

            var changed = !string.Equals(registration.CharacterName, newCharacterName, StringComparison.Ordinal)
                || registration.StartingEquipmentOptionId != newOptionId
                || !string.Equals(registration.CharacterPrepNote, newNote, StringComparison.Ordinal);

            if (!changed)
            {
                continue;
            }

            registration.CharacterName = newCharacterName;
            registration.StartingEquipmentOptionId = newOptionId;
            registration.CharacterPrepNote = newNote;
            registration.CharacterPrepUpdatedAtUtc = nowUtc;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RegistrationSubmission>> ListInvitationTargetsAsync(
        int gameId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.RegistrationSubmissions
            .AsNoTracking()
            .Where(x => x.GameId == gameId
                && x.CharacterPrepInvitedAtUtc == null
                && x.Registrations.Any(r => r.AttendeeType == AttendeeType.Player))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RegistrationSubmission>> ListReminderTargetsAsync(
        int gameId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var threshold = nowUtc.AddHours(-24);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.RegistrationSubmissions
            .AsNoTracking()
            .Where(x => x.GameId == gameId
                && x.CharacterPrepInvitedAtUtc != null
                && x.Registrations.Any(r =>
                    r.AttendeeType == AttendeeType.Player
                    && r.StartingEquipmentOptionId == null)
                && (x.CharacterPrepReminderLastSentAtUtc == null
                    || x.CharacterPrepReminderLastSentAtUtc < threshold))
            .ToListAsync(cancellationToken);
    }

    public async Task MarkInvitedAsync(
        int submissionId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var submission = await db.RegistrationSubmissions
            .FirstOrDefaultAsync(x => x.Id == submissionId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"RegistrationSubmission {submissionId} not found.");

        if (submission.CharacterPrepInvitedAtUtc is null)
        {
            submission.CharacterPrepInvitedAtUtc = nowUtc;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkReminderSentAsync(
        int submissionId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var submission = await db.RegistrationSubmissions
            .FirstOrDefaultAsync(x => x.Id == submissionId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"RegistrationSubmission {submissionId} not found.");

        submission.CharacterPrepReminderLastSentAtUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CharacterPrepStats> GetDashboardStatsAsync(
        int gameId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Fetch per-submission player aggregates in a single query so we can compute
        // TotalHouseholds / Invited / FullyFilled without N round trips.
        var aggregates = await db.RegistrationSubmissions
            .AsNoTracking()
            .Where(x => x.GameId == gameId)
            .Select(x => new
            {
                x.Id,
                x.CharacterPrepInvitedAtUtc,
                PlayerCount = x.Registrations.Count(r => r.AttendeeType == AttendeeType.Player),
                PlayersWithoutEquipment = x.Registrations.Count(r =>
                    r.AttendeeType == AttendeeType.Player
                    && r.StartingEquipmentOptionId == null),
                PlayersWithoutCharacterName = x.Registrations.Count(r =>
                    r.AttendeeType == AttendeeType.Player
                    && (r.CharacterName == null || r.CharacterName.Trim() == ""))
            })
            .ToListAsync(cancellationToken);

        var households = aggregates.Where(x => x.PlayerCount > 0).ToList();

        var total = households.Count;
        var invited = households.Count(x => x.CharacterPrepInvitedAtUtc is not null);
        var fullyFilled = households.Count(x =>
            x.PlayersWithoutEquipment == 0 && x.PlayersWithoutCharacterName == 0);
        var pending = total - fullyFilled;

        return new CharacterPrepStats(total, invited, fullyFilled, pending);
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
