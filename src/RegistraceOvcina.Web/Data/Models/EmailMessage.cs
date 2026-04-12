namespace RegistraceOvcina.Web.Data;

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
