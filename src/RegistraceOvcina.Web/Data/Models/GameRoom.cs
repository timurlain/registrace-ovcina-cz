namespace RegistraceOvcina.Web.Data;

public sealed class GameRoom
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int RoomId { get; set; }
    public int Capacity { get; set; }
    public Game Game { get; set; } = default!;
    public Room Room { get; set; } = default!;
}
