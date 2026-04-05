using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Kingdoms;

public sealed class KingdomAssignmentService(IDbContextFactory<ApplicationDbContext> dbContextFactory, TimeProvider timeProvider)
{
    public async Task<AssignmentBoard?> GetAssignmentBoardAsync(int gameId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var game = await db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == gameId, cancellationToken);

        if (game is null)
        {
            return null;
        }

        // Load kingdom targets for this game
        var kingdomTargets = await db.GameKingdomTargets
            .AsNoTracking()
            .Include(x => x.Kingdom)
            .Where(x => x.GameId == gameId)
            .OrderBy(x => x.Kingdom.Name)
            .ToListAsync(cancellationToken);

        // Load all player registrations from submitted submissions for this game
        var playerRegistrations = await db.Registrations
            .AsNoTracking()
            .Include(x => x.Person)
            .Include(x => x.Submission)
            .Include(x => x.PreferredKingdom)
            .Where(x => x.Submission.GameId == gameId
                && x.Submission.Status == SubmissionStatus.Submitted
                && x.AttendeeType == AttendeeType.Player
                && x.Status == RegistrationStatus.Active)
            .ToListAsync(cancellationToken);

        // Load existing character appearances (kingdom assignments) for this game
        var appearances = await db.CharacterAppearances
            .AsNoTracking()
            .Include(x => x.AssignedKingdom)
            .Where(x => x.GameId == gameId && x.RegistrationId != null)
            .ToListAsync(cancellationToken);

        var appearanceByRegistration = appearances
            .Where(x => x.RegistrationId.HasValue)
            .ToDictionary(x => x.RegistrationId!.Value);

        // Build player cards
        var playerCards = playerRegistrations.Select(r =>
        {
            appearanceByRegistration.TryGetValue(r.Id, out var appearance);
            return new PlayerCard
            {
                RegistrationId = r.Id,
                PersonName = $"{r.Person.FirstName} {r.Person.LastName}",
                BirthYear = r.Person.BirthYear,
                PlayerSubType = r.PlayerSubType,
                PreferredKingdomId = r.PreferredKingdomId,
                PreferredKingdomName = r.PreferredKingdom?.DisplayName,
                AssignedKingdomId = appearance?.AssignedKingdomId,
                CharacterName = r.CharacterName
            };
        }).ToList();

        // Build kingdom columns
        var columns = kingdomTargets.Select(kt => new KingdomColumn
        {
            KingdomId = kt.KingdomId,
            Name = kt.Kingdom.Name,
            DisplayName = kt.Kingdom.DisplayName,
            Color = kt.Kingdom.Color,
            TargetCount = kt.TargetPlayerCount,
            Players = playerCards.Where(p => p.AssignedKingdomId == kt.KingdomId).OrderBy(p => p.PersonName).ToList()
        }).ToList();

        var unassignedPlayers = playerCards
            .Where(p => p.AssignedKingdomId is null)
            .OrderBy(p => p.PersonName)
            .ToList();

        return new AssignmentBoard
        {
            GameId = gameId,
            GameName = game.Name,
            Kingdoms = columns,
            UnassignedPlayers = unassignedPlayers
        };
    }

    public async Task AssignPlayerAsync(int registrationId, int? kingdomId, string actorUserId, int? expectedGameId = null, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var registration = await db.Registrations
            .Include(x => x.Submission)
            .Include(x => x.Person)
            .FirstOrDefaultAsync(x => x.Id == registrationId, cancellationToken)
            ?? throw new InvalidOperationException("Registrace nebyla nalezena.");

        var gameId = registration.Submission.GameId;

        if (expectedGameId.HasValue && gameId != expectedGameId.Value)
            throw new InvalidOperationException("Registrace nepatří k zadané hře.");

        if (registration.Status != RegistrationStatus.Active)
            throw new InvalidOperationException("Registrace není aktivní.");

        if (registration.Submission.Status != SubmissionStatus.Submitted)
            throw new InvalidOperationException("Přihláška nebyla odeslána.");

        if (registration.AttendeeType != AttendeeType.Player)
            throw new InvalidOperationException("Království lze přiřadit pouze hráčům.");

        // Find existing CharacterAppearance for this registration+game
        var appearance = await db.CharacterAppearances
            .FirstOrDefaultAsync(x => x.RegistrationId == registrationId && x.GameId == gameId, cancellationToken);

        if (kingdomId is null)
        {
            // Unassign: remove kingdom from appearance
            if (appearance is not null)
            {
                appearance.AssignedKingdomId = null;
            }
        }
        else
        {
            if (appearance is null)
            {
                // Need to find or create a Character for this person
                var character = await db.Characters
                    .FirstOrDefaultAsync(x => x.PersonId == registration.PersonId && !x.IsDeleted, cancellationToken);

                if (character is null)
                {
                    character = new Character
                    {
                        PersonId = registration.PersonId,
                        Name = registration.CharacterName
                            ?? $"{registration.Person.FirstName} {registration.Person.LastName}",
                        IsDeleted = false
                    };
                    db.Characters.Add(character);
                    await db.SaveChangesAsync(cancellationToken);
                }

                appearance = new CharacterAppearance
                {
                    CharacterId = character.Id,
                    GameId = gameId,
                    RegistrationId = registrationId,
                    AssignedKingdomId = kingdomId,
                    ContinuityStatus = ContinuityStatus.Unknown
                };
                db.CharacterAppearances.Add(appearance);
            }
            else
            {
                appearance.AssignedKingdomId = kingdomId;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        if (appearance is not null)
        {
            db.AuditLogs.Add(new AuditLog
            {
                EntityType = nameof(CharacterAppearance),
                EntityId = appearance.Id.ToString(),
                Action = kingdomId is null ? "KingdomUnassigned" : "KingdomAssigned",
                ActorUserId = actorUserId,
                CreatedAtUtc = nowUtc,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    RegistrationId = registrationId,
                    GameId = gameId,
                    KingdomId = kingdomId,
                    PersonName = $"{registration.Person.FirstName} {registration.Person.LastName}"
                })
            });

            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

public sealed class AssignmentBoard
{
    public int GameId { get; set; }
    public string GameName { get; set; } = "";
    public List<KingdomColumn> Kingdoms { get; set; } = [];
    public List<PlayerCard> UnassignedPlayers { get; set; } = [];
}

public sealed class KingdomColumn
{
    public int KingdomId { get; set; }
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Color { get; set; }
    public int TargetCount { get; set; }
    public List<PlayerCard> Players { get; set; } = [];
    public int CurrentCount => Players.Count;
}

public sealed class PlayerCard
{
    public int RegistrationId { get; set; }
    public string PersonName { get; set; } = "";
    public int BirthYear { get; set; }
    public PlayerSubType? PlayerSubType { get; set; }
    public int? PreferredKingdomId { get; set; }
    public string? PreferredKingdomName { get; set; }
    public int? AssignedKingdomId { get; set; }
    public string? CharacterName { get; set; }
}
