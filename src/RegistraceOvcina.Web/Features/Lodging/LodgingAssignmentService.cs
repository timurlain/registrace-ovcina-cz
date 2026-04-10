using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Lodging;

public sealed class LodgingAssignmentService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    TimeProvider timeProvider)
{
    public async Task<LodgingBoard?> GetAssignmentBoardAsync(int gameId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var game = await db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == gameId, cancellationToken);

        if (game is null)
        {
            return null;
        }

        // Load game rooms for this game
        var gameRooms = await db.GameRooms
            .AsNoTracking()
            .Include(x => x.Room)
            .Where(x => x.GameId == gameId)
            .OrderBy(x => x.Room.Name)
            .ToListAsync(cancellationToken);

        // Load all registrations with Indoor lodging preference for this game
        var registrations = await db.Registrations
            .AsNoTracking()
            .Include(x => x.Person)
            .Include(x => x.Submission)
            .Where(x => x.Submission.GameId == gameId
                && x.Submission.Status == SubmissionStatus.Submitted
                && x.Status == RegistrationStatus.Active
                && x.LodgingPreference == LodgingPreference.Indoor)
            .ToListAsync(cancellationToken);

        // Build lodging cards
        var cards = registrations.Select(r => new LodgingCard
        {
            RegistrationId = r.Id,
            PersonName = $"{r.Person.FirstName} {r.Person.LastName}",
            BirthYear = r.Person.BirthYear,
            GroupName = r.Submission.GroupName,
            PersonNotes = r.Person.Notes,
            RegistrantNote = r.RegistrantNote,
            AssignedGameRoomId = r.AssignedGameRoomId
        })
        .OrderBy(c => c.GroupName)
        .ThenBy(c => c.PersonName)
        .ToList();

        // Build room columns
        var columns = gameRooms.Select(gr => new RoomColumn
        {
            GameRoomId = gr.Id,
            RoomName = gr.Room.Name,
            Capacity = gr.Capacity,
            Guests = cards.Where(c => c.AssignedGameRoomId == gr.Id)
                .OrderBy(c => c.GroupName)
                .ThenBy(c => c.PersonName)
                .ToList()
        }).ToList();

        var unassignedGuests = cards
            .Where(c => c.AssignedGameRoomId is null)
            .ToList();

        return new LodgingBoard
        {
            GameId = gameId,
            GameName = game.Name,
            Rooms = columns,
            UnassignedGuests = unassignedGuests
        };
    }

    public async Task AssignToRoomAsync(int registrationId, int? gameRoomId, string actorUserId, int? expectedGameId = null, CancellationToken cancellationToken = default)
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
            throw new InvalidOperationException("Registrace nepat\u0159\u00ed k zadan\u00e9 h\u0159e.");

        if (registration.Status != RegistrationStatus.Active)
            throw new InvalidOperationException("Registrace nen\u00ed aktivn\u00ed.");

        if (registration.Submission.Status != SubmissionStatus.Submitted)
            throw new InvalidOperationException("P\u0159ihl\u00e1\u0161ka nebyla odesl\u00e1na.");

        if (registration.LodgingPreference != LodgingPreference.Indoor)
            throw new InvalidOperationException("Ubytov\u00e1n\u00ed v budov\u011b lze p\u0159i\u0159adit pouze \u00fa\u010dastn\u00edk\u016fm s preferencí Indoor.");

        registration.AssignedGameRoomId = gameRoomId;

        await db.SaveChangesAsync(cancellationToken);

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(Registration),
            EntityId = registration.Id.ToString(),
            Action = gameRoomId is null ? "RoomUnassigned" : "RoomAssigned",
            ActorUserId = actorUserId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                RegistrationId = registrationId,
                GameId = gameId,
                GameRoomId = gameRoomId,
                PersonName = $"{registration.Person.FirstName} {registration.Person.LastName}"
            })
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class LodgingBoard
{
    public int GameId { get; set; }
    public string GameName { get; set; } = "";
    public List<RoomColumn> Rooms { get; set; } = [];
    public List<LodgingCard> UnassignedGuests { get; set; } = [];
}

public sealed class RoomColumn
{
    public int GameRoomId { get; set; }
    public string RoomName { get; set; } = "";
    public int Capacity { get; set; }
    public List<LodgingCard> Guests { get; set; } = [];
    public int CurrentCount => Guests.Count;
}

public sealed class LodgingCard
{
    public int RegistrationId { get; set; }
    public string PersonName { get; set; } = "";
    public int BirthYear { get; set; }
    public string? GroupName { get; set; }
    public string? PersonNotes { get; set; }
    public string? RegistrantNote { get; set; }
    public int? AssignedGameRoomId { get; set; }

    public int Age(int currentYear) => currentYear - BirthYear;
}
