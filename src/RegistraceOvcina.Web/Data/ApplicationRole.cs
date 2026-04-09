using Microsoft.AspNetCore.Identity;

namespace RegistraceOvcina.Web.Data;

public sealed class ApplicationRole : IdentityRole
{
    public string Category { get; set; } = "system";
}
