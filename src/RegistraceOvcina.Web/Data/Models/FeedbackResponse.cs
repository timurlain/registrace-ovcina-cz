namespace RegistraceOvcina.Web.Data;

public sealed class FeedbackResponse
{
    public int Id { get; set; }
    public int RegistrationId { get; set; }
    public Registration Registration { get; set; } = null!;

    public string AnswersJson { get; set; } = "{}";

    public FeedbackEditedBy LastEditedBy { get; set; } = FeedbackEditedBy.Self;
    public int? LastEditedByPersonId { get; set; }
    public Person? LastEditedByPerson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }

    public bool PinToChronicle { get; set; }
    public string? OrganizerNote { get; set; }
}
