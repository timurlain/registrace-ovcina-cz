namespace RegistraceOvcina.Web.Data;

public sealed class UserEmail
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public ApplicationUser User { get; set; } = default!;
}
