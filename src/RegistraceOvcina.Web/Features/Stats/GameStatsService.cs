using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Stats;

public sealed class GameStatsService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<GameStats?> GetGameStatsAsync(int gameId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var game = await db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == gameId, cancellationToken);

        if (game is null)
        {
            return null;
        }

        var currentYear = DateTime.UtcNow.Year;

        // --- Submission counts (all statuses, directly from submissions) ---
        var allSubmissions = await db.RegistrationSubmissions
            .AsNoTracking()
            .Where(x => x.GameId == gameId)
            .Select(x => new { x.Id, x.Status, x.ExpectedTotalAmount, x.VoluntaryDonation })
            .ToListAsync(cancellationToken);

        var submittedCount = allSubmissions.Count(x => x.Status == SubmissionStatus.Submitted);
        var draftCount = allSubmissions.Count(x => x.Status == SubmissionStatus.Draft);
        var cancelledCount = allSubmissions.Count(x => x.Status == SubmissionStatus.Cancelled);

        // --- Active registrations from submitted submissions ---
        var registrations = await db.Registrations
            .AsNoTracking()
            .Include(x => x.Person)
            .Include(x => x.Submission)
            .Include(x => x.FoodOrders).ThenInclude(fo => fo.MealOption)
            .Where(x => x.Submission.GameId == gameId
                && x.Submission.Status == SubmissionStatus.Submitted
                && x.Status == RegistrationStatus.Active)
            .ToListAsync(cancellationToken);

        var totalAttendees = registrations.Count;

        // --- Players by sub-type ---
        var players = registrations.Where(x => x.AttendeeType == AttendeeType.Player).ToList();
        var playerCount = players.Count;
        var playerPvp = players.Count(x => x.PlayerSubType == PlayerSubType.Pvp);
        var playerIndependent = players.Count(x => x.PlayerSubType == PlayerSubType.Independent);
        var playerWithRanger = players.Count(x => x.PlayerSubType == PlayerSubType.WithRanger);
        var playerWithParent = players.Count(x => x.PlayerSubType == PlayerSubType.WithParent);

        // --- Adults by role ---
        var adults = registrations.Where(x => x.AttendeeType == AttendeeType.Adult).ToList();
        var adultNpc = adults.Count(x => x.AdultRoles.HasFlag(AdultRoleFlags.PlayMonster));
        var adultHelper = adults.Count(x =>
            x.AdultRoles.HasFlag(AdultRoleFlags.OrganizationHelper)
            || x.AdultRoles.HasFlag(AdultRoleFlags.TechSupport)
            || x.AdultRoles.HasFlag(AdultRoleFlags.RangerLeader));
        var adultMonster = adults.Count(x => x.AdultRoles.HasFlag(AdultRoleFlags.Spectator));

        // --- Demographics ---
        var playerAges = players.Select(x => currentYear - x.Person.BirthYear).ToList();
        var averagePlayerAge = playerAges.Count > 0 ? playerAges.Average() : 0;

        var submittedSubmissionIds = allSubmissions
            .Where(x => x.Status == SubmissionStatus.Submitted)
            .Select(x => x.Id)
            .ToHashSet();

        var groupSizes = registrations
            .Where(x => submittedSubmissionIds.Contains(x.SubmissionId))
            .GroupBy(x => x.SubmissionId)
            .Select(g => g.Count())
            .ToList();
        var averageGroupSize = groupSizes.Count > 0 ? groupSizes.Average() : 0;

        // --- Age brackets ---
        var ageBrackets = BuildAgeBrackets(playerAges);

        // --- Lodging ---
        var lodgingIndoor = registrations.Count(x => x.LodgingPreference == LodgingPreference.Indoor);
        var lodgingTent = registrations.Count(x => x.LodgingPreference == LodgingPreference.OwnTent);
        var lodgingOutdoor = registrations.Count(x => x.LodgingPreference == LodgingPreference.CampOutdoor);
        var lodgingNotStaying = registrations.Count(x => x.LodgingPreference == LodgingPreference.NotStaying);

        // --- Room occupancy ---
        var gameRooms = await db.GameRooms
            .AsNoTracking()
            .Include(x => x.Room)
            .Where(x => x.GameId == gameId)
            .OrderBy(x => x.Room.Name)
            .ToListAsync(cancellationToken);

        var roomAssignments = registrations
            .Where(x => x.AssignedGameRoomId.HasValue)
            .GroupBy(x => x.AssignedGameRoomId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var roomOccupancies = gameRooms.Select(gr => new RoomOccupancy(
            gr.Room.Name,
            roomAssignments.GetValueOrDefault(gr.Id),
            gr.Capacity)).ToList();

        var roomsAssigned = roomAssignments.Values.Sum();
        var roomsTotal = gameRooms.Sum(x => x.Capacity);

        // --- Kingdoms ---
        var kingdomTargets = await db.GameKingdomTargets
            .AsNoTracking()
            .Include(x => x.Kingdom)
            .Where(x => x.GameId == gameId)
            .OrderBy(x => x.Kingdom.Name)
            .ToListAsync(cancellationToken);

        var appearances = await db.CharacterAppearances
            .AsNoTracking()
            .Include(x => x.Registration!).ThenInclude(r => r.Person)
            .Where(x => x.GameId == gameId
                && x.RegistrationId != null
                && x.AssignedKingdomId != null)
            .ToListAsync(cancellationToken);

        var assignmentsByKingdom = appearances
            .Where(x => x.Registration is not null)
            .GroupBy(x => x.AssignedKingdomId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.ToList());

        var kingdoms = kingdomTargets.Select(kt =>
        {
            var assigned = assignmentsByKingdom.GetValueOrDefault(kt.KingdomId, []);
            var ages = assigned
                .Where(a => a.Registration?.Person is not null)
                .Select(a => currentYear - a.Registration!.Person.BirthYear)
                .ToList();
            return new KingdomStat(
                kt.Kingdom.DisplayName,
                kt.Kingdom.Color,
                assigned.Count,
                kt.TargetPlayerCount,
                ages.Count > 0 ? Math.Round(ages.Average(), 1) : 0);
        }).ToList();

        var totalAssignedPlayers = appearances.Count;
        var kingdomUnassigned = playerCount - totalAssignedPlayers;

        // --- Financial ---
        var submittedSubmissions = await db.RegistrationSubmissions
            .AsNoTracking()
            .Include(x => x.Payments)
            .Where(x => x.GameId == gameId && x.Status == SubmissionStatus.Submitted)
            .ToListAsync(cancellationToken);

        var expectedTotal = submittedSubmissions.Sum(x => x.ExpectedTotalAmount);
        var paidTotal = submittedSubmissions.Sum(x => x.Payments.Sum(p => p.Amount));
        var donationTotal = submittedSubmissions.Sum(x => x.VoluntaryDonation);
        var unpaidSubmissionCount = submittedSubmissions
            .Count(x => x.Payments.Sum(p => p.Amount) < x.ExpectedTotalAmount && x.ExpectedTotalAmount > 0);

        // Named donor breakdown, sorted largest-first. Only submissions with an
        // actual voluntary contribution are included.
        var donors = submittedSubmissions
            .Where(x => x.VoluntaryDonation > 0m)
            .OrderByDescending(x => x.VoluntaryDonation)
            .ThenBy(x => x.PrimaryContactName, StringComparer.CurrentCulture)
            .Select(x => new DonorEntry(
                x.Id,
                string.IsNullOrWhiteSpace(x.PrimaryContactName) ? "(bez jména)" : x.PrimaryContactName,
                x.PrimaryEmail,
                x.VoluntaryDonation))
            .ToList();

        // --- Food / Meals ---
        var allFoodOrders = registrations.SelectMany(x => x.FoodOrders).ToList();
        var meals = allFoodOrders
            .GroupBy(fo => new { Day = fo.MealDayUtc.Date, MealName = fo.MealOption.Name })
            .OrderBy(g => g.Key.Day).ThenBy(g => g.Key.MealName)
            .Select(g =>
            {
                var childCount = g.Count(fo =>
                    registrations.First(r => r.Id == fo.RegistrationId).AttendeeType == AttendeeType.Player);
                var adultCount = g.Count() - childCount;
                return new MealStat(
                    g.Key.Day.ToString("dd.MM. dddd"),
                    g.Key.MealName,
                    childCount,
                    adultCount);
            })
            .ToList();

        // --- Action items ---
        var indoorWithoutRoom = registrations
            .Count(x => x.LodgingPreference == LodgingPreference.Indoor && !x.AssignedGameRoomId.HasValue);
        var playersWithoutKingdom = Math.Max(0, kingdomUnassigned);

        // --- Character prep widget ---
        // Total = every Player row; Filled = Players with BOTH a non-blank character name
        // AND a chosen StartingEquipmentOptionId. Matches the dashboard's "Done" state
        // so the widget and dashboard can't disagree. Soft-deleted submissions are
        // excluded by the Registration.HasQueryFilter(!Submission.IsDeleted) that
        // propagates through the db.Registrations query above — locked in by
        // GameStatsCharacterPrepWidgetTests.Widget_excludes_soft_deleted_submissions.
        var characterPrepTotal = players.Count;
        var characterPrepFilled = players.Count(p =>
            !string.IsNullOrWhiteSpace(p.CharacterName)
            && p.StartingEquipmentOptionId.HasValue);

        return new GameStats
        {
            GameName = game.Name,
            SubmittedCount = submittedCount,
            DraftCount = draftCount,
            CancelledCount = cancelledCount,
            TotalAttendees = totalAttendees,
            PlayerCount = playerCount,
            PlayerPvp = playerPvp,
            PlayerIndependent = playerIndependent,
            PlayerWithRanger = playerWithRanger,
            PlayerWithParent = playerWithParent,
            AdultNpc = adultNpc,
            AdultMonster = adultMonster,
            AdultHelper = adultHelper,
            AgeBrackets = ageBrackets,
            AveragePlayerAge = Math.Round(averagePlayerAge, 1),
            AverageGroupSize = Math.Round(averageGroupSize, 1),
            LodgingIndoor = lodgingIndoor,
            LodgingTent = lodgingTent,
            LodgingOutdoor = lodgingOutdoor,
            LodgingNotStaying = lodgingNotStaying,
            RoomsAssigned = roomsAssigned,
            RoomsTotal = roomsTotal,
            RoomOccupancies = roomOccupancies,
            Kingdoms = kingdoms,
            KingdomUnassigned = playersWithoutKingdom,
            ExpectedTotal = expectedTotal,
            PaidTotal = paidTotal,
            DonationTotal = donationTotal,
            Donors = donors,
            UnpaidSubmissionCount = unpaidSubmissionCount,
            Meals = meals,
            IndoorWithoutRoom = indoorWithoutRoom,
            PlayersWithoutKingdom = playersWithoutKingdom,
            CharacterPrepFilled = characterPrepFilled,
            CharacterPrepTotal = characterPrepTotal
        };
    }

    private static List<AgeBracket> BuildAgeBrackets(List<int> ages)
    {
        var brackets = new (string Label, Func<int, bool> Predicate)[]
        {
            ("6–9", a => a >= 6 && a <= 9),
            ("10–12", a => a >= 10 && a <= 12),
            ("13–15", a => a >= 13 && a <= 15),
            ("16–17", a => a >= 16 && a <= 17),
            ("18+", a => a >= 18)
        };

        var counts = brackets
            .Select(b => new { b.Label, Count = ages.Count(b.Predicate) })
            .ToList();

        var maxCount = counts.Max(x => x.Count);
        if (maxCount == 0) maxCount = 1; // avoid division by zero

        return counts
            .Select(x => new AgeBracket(x.Label, x.Count, maxCount))
            .ToList();
    }
}

// --- DTOs ---

public sealed class GameStats
{
    public string GameName { get; set; } = "";

    // Registration overview
    public int SubmittedCount { get; set; }
    public int DraftCount { get; set; }
    public int CancelledCount { get; set; }
    public int TotalAttendees { get; set; }

    // Players by sub-type
    public int PlayerCount { get; set; }
    public int PlayerPvp { get; set; }
    public int PlayerIndependent { get; set; }
    public int PlayerWithRanger { get; set; }
    public int PlayerWithParent { get; set; }

    // Adults by type
    public int AdultNpc { get; set; }
    public int AdultMonster { get; set; }
    public int AdultHelper { get; set; }

    // Demographics
    public List<AgeBracket> AgeBrackets { get; set; } = [];
    public double AveragePlayerAge { get; set; }
    public double AverageGroupSize { get; set; }

    // Lodging
    public int LodgingIndoor { get; set; }
    public int LodgingTent { get; set; }
    public int LodgingOutdoor { get; set; }
    public int LodgingNotStaying { get; set; }
    public int RoomsAssigned { get; set; }
    public int RoomsTotal { get; set; }
    public List<RoomOccupancy> RoomOccupancies { get; set; } = [];

    // Kingdoms
    public List<KingdomStat> Kingdoms { get; set; } = [];
    public int KingdomUnassigned { get; set; }

    // Financial
    public decimal ExpectedTotal { get; set; }
    public decimal PaidTotal { get; set; }
    public decimal DonationTotal { get; set; }
    public List<DonorEntry> Donors { get; set; } = [];
    public int UnpaidSubmissionCount { get; set; }

    // Food
    public List<MealStat> Meals { get; set; } = [];

    // Action items
    public int IndoorWithoutRoom { get; set; }
    public int PlayersWithoutKingdom { get; set; }

    // Character prep progress (widget on /statistiky links to dashboard)
    public int CharacterPrepFilled { get; set; }
    public int CharacterPrepTotal { get; set; }
}

public sealed record AgeBracket(string Label, int Count, int MaxCount);
public sealed record RoomOccupancy(string RoomName, int Assigned, int Capacity);
public sealed record KingdomStat(string Name, string? Color, int Assigned, int Target, double AverageAge);
public sealed record MealStat(string Day, string MealName, int ChildCount, int AdultCount);
public sealed record DonorEntry(int SubmissionId, string ContactName, string? ContactEmail, decimal Amount);
