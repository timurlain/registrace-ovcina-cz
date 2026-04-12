namespace RegistraceOvcina.Web.Data;

public sealed class GameInvitation
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public string RecipientEmail { get; set; } = "";
    public string RecipientName { get; set; } = "";
    public string SentByUserId { get; set; } = "";
    public DateTime SentAtUtc { get; set; }
    public string Subject { get; set; } = "";
    public string? Note { get; set; }
    public Game Game { get; set; } = default!;
}
