namespace RegistraceOvcina.Web.Data;

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
