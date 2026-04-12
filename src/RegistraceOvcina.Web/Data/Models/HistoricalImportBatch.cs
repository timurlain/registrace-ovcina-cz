namespace RegistraceOvcina.Web.Data;

public sealed class HistoricalImportBatch
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public string SourceFormat { get; set; } = "";
    public string SourceFileName { get; set; } = "";
    public int GameId { get; set; }
    public string ImportedByUserId { get; set; } = "";
    public DateTime ImportedAtUtc { get; set; }
    public int TotalSourceRows { get; set; }
    public int HouseholdCount { get; set; }
    public int RegistrationCount { get; set; }
    public int PersonCreatedCount { get; set; }
    public int PersonMatchedCount { get; set; }
    public int CharacterCreatedCount { get; set; }
    public int WarningCount { get; set; }
    public string? NotesJson { get; set; }
    public Game Game { get; set; } = default!;
    public List<HistoricalImportRow> Rows { get; set; } = [];
}
