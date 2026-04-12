namespace RegistraceOvcina.Web.Data;

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
