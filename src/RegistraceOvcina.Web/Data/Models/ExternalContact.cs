namespace RegistraceOvcina.Web.Data;

public sealed class ExternalContact
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
