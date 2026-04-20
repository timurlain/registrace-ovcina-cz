namespace RegistraceOvcina.Web.Data;

public sealed class RegistrationSubmission
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public string RegistrantUserId { get; set; } = "";
    public string PrimaryContactName { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string PrimaryEmail { get; set; } = "";
    public string PrimaryPhone { get; set; } = "";
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Draft;
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime LastEditedAtUtc { get; set; }
    public decimal ExpectedTotalAmount { get; set; }
    public string? RegistrantNote { get; set; }
    public decimal VoluntaryDonation { get; set; }
    public bool IsDeleted { get; set; }
    public string? PaymentVariableSymbol { get; set; }
    public string? CharacterPrepToken { get; set; }
    public DateTimeOffset? CharacterPrepInvitedAtUtc { get; set; }
    public DateTimeOffset? CharacterPrepReminderLastSentAtUtc { get; set; }
    public Game Game { get; set; } = default!;
    public List<Registration> Registrations { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
    public List<OrganizerNote> OrganizerNotes { get; set; } = [];
}
