namespace RegistraceOvcina.Web.Features.Auth;

public sealed class GuestAuthOptions
{
    public const string SectionName = "GuestAuth";
    public bool Enabled { get; set; }
    public string PinHash { get; set; } = "";
}
