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

/// <summary>
/// Derived status of a single Player registration on the organizer dashboard.
/// Order is load-bearing: the dashboard sorts ascending so organizers see rows
/// that need attention first (NotInvited → Waiting → Done).
/// </summary>
public enum CharacterPrepStatus
{
    NotInvited = 0,
    Waiting = 1,
    Done = 2,
}

public sealed record CharacterPrepDashboardRow(
    int SubmissionId,
    string HouseholdName,
    int RegistrationId,
    string PersonFullName,
    string? CharacterName,
    string? EquipmentDisplayName,
    string? CharacterPrepNote,
    CharacterPrepStatus Status,
    DateTimeOffset? UpdatedAtUtc,
    string? SubmissionToken);

public sealed record DashboardFilter(
    CharacterPrepStatus? Status,
    string? Search);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Rows,
    int Total,
    int Page,
    int PageSize);
