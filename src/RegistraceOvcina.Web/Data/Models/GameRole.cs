namespace RegistraceOvcina.Web.Data;

public sealed class GameRole
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public int GameId { get; set; }
    public string RoleName { get; set; } = "";
    public DateTime AssignedAtUtc { get; set; }
    public ApplicationUser User { get; set; } = default!;
    public Game Game { get; set; } = default!;
}
