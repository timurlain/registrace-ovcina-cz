namespace RegistraceOvcina.Web.Data;

public sealed class LoginToken
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string? UserId { get; set; }
    public string Token { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ApplicationUser? User { get; set; }
}
