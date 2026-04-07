namespace RegistraceOvcina.Web.Features.Auth;

public sealed class AcsEmailOptions
{
    public const string SectionName = "AzureCommunication";

    public string? ConnectionString { get; set; }
    public string? SenderAddress { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ConnectionString) &&
        !string.IsNullOrWhiteSpace(SenderAddress);
}
