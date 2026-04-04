using Microsoft.AspNetCore.Identity;

namespace RegistraceOvcina.Web.Data;

public sealed class ApplicationUser : IdentityUser
{
    [PersonalData]
    public string DisplayName { get; set; } = "";

    [PersonalData]
    public int? PersonId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? LastLoginAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
