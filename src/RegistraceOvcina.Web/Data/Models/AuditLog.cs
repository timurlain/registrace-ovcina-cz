namespace RegistraceOvcina.Web.Data;

public sealed class AuditLog
{
    public int Id { get; set; }
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Action { get; set; } = "";
    public string ActorUserId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string? DetailsJson { get; set; }
}
