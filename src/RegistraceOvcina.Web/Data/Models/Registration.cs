namespace RegistraceOvcina.Web.Data;

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
    public int? AssignedGameRoomId { get; set; }
    public GameRoom? AssignedGameRoom { get; set; }
    public List<FoodOrder> FoodOrders { get; set; } = [];
}
