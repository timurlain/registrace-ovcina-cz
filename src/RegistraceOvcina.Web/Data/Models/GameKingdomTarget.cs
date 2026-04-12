namespace RegistraceOvcina.Web.Data;

public sealed class GameKingdomTarget
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int KingdomId { get; set; }
    public int TargetPlayerCount { get; set; }
    public Game Game { get; set; } = default!;
    public Kingdom Kingdom { get; set; } = default!;
}
