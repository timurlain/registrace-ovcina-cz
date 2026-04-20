namespace RegistraceOvcina.Web.Features.CharacterPrep;

public sealed record CharacterPrepView(
    int SubmissionId,
    int GameId,
    string GameName,
    DateTimeOffset GameStartDateUtc,
    bool IsReadOnly,
    IReadOnlyList<CharacterPrepRow> Rows,
    IReadOnlyList<StartingEquipmentOptionView> Options);

public sealed record CharacterPrepRow(
    int RegistrationId,
    string PersonFullName,
    string? CharacterName,
    int? StartingEquipmentOptionId,
    string? CharacterPrepNote,
    DateTimeOffset? UpdatedAtUtc);

public sealed record StartingEquipmentOptionView(
    int Id,
    string Key,
    string DisplayName,
    string? Description,
    int SortOrder);

public sealed record CharacterPrepSaveRow(
    int RegistrationId,
    string? CharacterName,
    int? StartingEquipmentOptionId,
    string? CharacterPrepNote);

public sealed record CharacterPrepStats(
    int TotalHouseholds,
    int Invited,
    int FullyFilled,
    int Pending);
