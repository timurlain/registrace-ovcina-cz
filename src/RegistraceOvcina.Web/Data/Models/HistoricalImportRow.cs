namespace RegistraceOvcina.Web.Data;

public sealed class HistoricalImportRow
{
    public int Id { get; set; }
    public int? LastBatchId { get; set; }
    public string SourceFormat { get; set; } = "";
    public string SourceSheet { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public string SourceLabel { get; set; } = "";
    public int? LinkedPersonId { get; set; }
    public int? LinkedSubmissionId { get; set; }
    public int? LinkedRegistrationId { get; set; }
    public int? LinkedCharacterId { get; set; }
    public string? WarningMessage { get; set; }
    public DateTime FirstImportedAtUtc { get; set; }
    public DateTime LastImportedAtUtc { get; set; }
    public HistoricalImportBatch? LastBatch { get; set; }
    public Person? LinkedPerson { get; set; }
    public RegistrationSubmission? LinkedSubmission { get; set; }
    public Registration? LinkedRegistration { get; set; }
    public Character? LinkedCharacter { get; set; }
}
