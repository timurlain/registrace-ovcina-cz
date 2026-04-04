namespace RegistraceOvcina.Web.Data;

public enum BalanceStatus
{
    Unpaid = 0,
    Underpaid = 1,
    Balanced = 2,
    Overpaid = 3
}

public enum ContinuityStatus
{
    Unknown = 0,
    Continued = 1,
    Retired = 2
}

public enum EmailDirection
{
    Inbound = 0,
    Outbound = 1
}

public enum PaymentMethod
{
    BankTransfer = 0,
    Cash = 1,
    ManualAdjustment = 2
}

public enum AttendeeType
{
    Player = 0,
    Adult = 1
}

public enum PlayerSubType
{
    /// <summary>hrající samostatné dítě (cca 10+, PVP hráč)</summary>
    Pvp = 0,
    /// <summary>hrající samostatné dítě (cca 8+)</summary>
    Independent = 1,
    /// <summary>hrající dítě ve skupince s hraničárem (cca 5–7)</summary>
    WithRanger = 2,
    /// <summary>hrající dítě v doprovodu rodiče (cca 4+)</summary>
    WithParent = 3
}

[Flags]
public enum AdultRoleFlags
{
    None = 0,
    /// <summary>hrát cizí postavu / příšeru (skřet, kostlivec, vlci…)</summary>
    PlayMonster = 1,
    /// <summary>pomoci organizaci (obchodník, příručí ve městech)</summary>
    OrganizationHelper = 2,
    /// <summary>technická organizace (svačiny, rozvoz jídla, spojka)</summary>
    TechSupport = 4,
    /// <summary>vést skupinku menších dětí (hraničář)</summary>
    RangerLeader = 8,
    /// <summary>pouze přihlížející</summary>
    Spectator = 16
}

public enum LodgingPreference
{
    /// <summary>Chci spát uvnitř (budova)</summary>
    Indoor = 0,
    /// <summary>Mám vlastní stan</summary>
    OwnTent = 1,
    /// <summary>Mohu spát venku / pod širákem</summary>
    CampOutdoor = 2,
    /// <summary>Neplánuji přenocovat</summary>
    NotStaying = 3
}

public enum RegistrationStatus
{
    Active = 0,
    Cancelled = 1
}

public enum SubmissionStatus
{
    Draft = 0,
    Submitted = 1,
    Cancelled = 2
}

public enum VariableSymbolStrategy
{
    PerSubmissionId = 0,
    SequentialPerGame = 1,
    ManualOverride = 2
}

public sealed class Person
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public int BirthYear { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<Character> Characters { get; set; } = [];
    public List<Registration> Registrations { get; set; } = [];
    public List<OrganizerNote> OrganizerNotes { get; set; } = [];
}

public sealed class Game
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public DateTime RegistrationClosesAtUtc { get; set; }
    public DateTime MealOrderingClosesAtUtc { get; set; }
    public DateTime PaymentDueAtUtc { get; set; }
    public DateTime? AssignmentFreezeAtUtc { get; set; }
    public decimal PlayerBasePrice { get; set; }
    public decimal AdultHelperBasePrice { get; set; }
    public string BankAccount { get; set; } = "";
    public string BankAccountName { get; set; } = "";
    public VariableSymbolStrategy VariableSymbolStrategy { get; set; }
    public int TargetPlayerCountTotal { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<GameKingdomTarget> KingdomTargets { get; set; } = [];
    public List<MealOption> MealOptions { get; set; } = [];
    public List<RegistrationSubmission> Submissions { get; set; } = [];
}

public sealed class Kingdom
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Color { get; set; }
}

public sealed class GameKingdomTarget
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int KingdomId { get; set; }
    public int TargetPlayerCount { get; set; }
    public Game Game { get; set; } = default!;
    public Kingdom Kingdom { get; set; } = default!;
}

public sealed class RegistrationSubmission
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public string RegistrantUserId { get; set; } = "";
    public string PrimaryContactName { get; set; } = "";
    public string PrimaryEmail { get; set; } = "";
    public string PrimaryPhone { get; set; } = "";
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Draft;
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime LastEditedAtUtc { get; set; }
    public decimal ExpectedTotalAmount { get; set; }
    public string? RegistrantNote { get; set; }
    public bool IsDeleted { get; set; }
    public string? PaymentVariableSymbol { get; set; }
    public Game Game { get; set; } = default!;
    public List<Registration> Registrations { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
    public List<OrganizerNote> OrganizerNotes { get; set; } = [];
}

public sealed class Registration
{
    public int Id { get; set; }
    public int SubmissionId { get; set; }
    public int PersonId { get; set; }
    public AttendeeType AttendeeType { get; set; }
    public PlayerSubType? PlayerSubType { get; set; }
    public AdultRoleFlags AdultRoles { get; set; }
    public string? CharacterName { get; set; }
    public LodgingPreference? LodgingPreference { get; set; }
    public RegistrationStatus Status { get; set; } = RegistrationStatus.Active;
    public int? PreferredKingdomId { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? GuardianName { get; set; }
    public string? GuardianRelationship { get; set; }
    public bool GuardianAuthorizationConfirmed { get; set; }
    public string? RegistrantNote { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public RegistrationSubmission Submission { get; set; } = default!;
    public Person Person { get; set; } = default!;
    public Kingdom? PreferredKingdom { get; set; }
    public List<FoodOrder> FoodOrders { get; set; } = [];
}

public sealed class Character
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public string Name { get; set; } = "";
    public string? Race { get; set; }
    public string? ClassOrType { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
    public Person Person { get; set; } = default!;
    public List<CharacterAppearance> Appearances { get; set; } = [];
}

public sealed class CharacterAppearance
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public int GameId { get; set; }
    public int? RegistrationId { get; set; }
    public int? LevelReached { get; set; }
    public int? AssignedKingdomId { get; set; }
    public ContinuityStatus ContinuityStatus { get; set; } = ContinuityStatus.Unknown;
    public string? Notes { get; set; }
    public Character Character { get; set; } = default!;
    public Game Game { get; set; } = default!;
    public Registration? Registration { get; set; }
    public Kingdom? AssignedKingdom { get; set; }
}

public sealed class MealOption
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public Game Game { get; set; } = default!;
}

public sealed class FoodOrder
{
    public int Id { get; set; }
    public int RegistrationId { get; set; }
    public int MealOptionId { get; set; }
    public DateTime MealDayUtc { get; set; }
    public decimal Price { get; set; }
    public Registration Registration { get; set; } = default!;
    public MealOption MealOption { get; set; } = default!;
}

public sealed class Payment
{
    public int Id { get; set; }
    public int SubmissionId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "CZK";
    public DateTime RecordedAtUtc { get; set; }
    public string? RecordedByUserId { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.BankTransfer;
    public string? Reference { get; set; }
    public string? Note { get; set; }
    public RegistrationSubmission Submission { get; set; } = default!;
}

public sealed class OrganizerNote
{
    public int Id { get; set; }
    public int? SubmissionId { get; set; }
    public int? PersonId { get; set; }
    public string AuthorUserId { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public RegistrationSubmission? Submission { get; set; }
    public Person? Person { get; set; }
}

public sealed class EmailMessage
{
    public int Id { get; set; }
    public string MailboxItemId { get; set; } = "";
    public EmailDirection Direction { get; set; }
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? BodyText { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public int? LinkedSubmissionId { get; set; }
    public int? LinkedPersonId { get; set; }
    public string? AttachmentMetadataJson { get; set; }
    public RegistrationSubmission? LinkedSubmission { get; set; }
    public Person? LinkedPerson { get; set; }
}

public sealed class AuditLog
{
    public int Id { get; set; }
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Action { get; set; } = "";
    public string ActorUserId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string? DetailsJson { get; set; }
}
