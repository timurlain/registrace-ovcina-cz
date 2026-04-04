namespace RegistraceOvcina.Web.Features.Email;

public sealed class MailboxEmailOptions
{
    public const string SectionName = "Email";
    public const string ValidationMessage =
        "Configure Email:SharedMailboxAddress and Email:Graph:TenantId/ClientId/ClientSecret together.";

    public string? SharedMailboxAddress { get; set; }

    public MicrosoftGraphOptions Graph { get; set; } = new();

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SharedMailboxAddress) &&
        Graph.IsConfigured;

    public bool HasAnyConfiguration =>
        !string.IsNullOrWhiteSpace(SharedMailboxAddress) ||
        Graph.HasAnyConfiguration;

    public bool HasPartialConfiguration => HasAnyConfiguration && !IsConfigured;
}

public sealed class MicrosoftGraphOptions
{
    public string? TenantId { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId) &&
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);

    public bool HasAnyConfiguration =>
        !string.IsNullOrWhiteSpace(TenantId) ||
        !string.IsNullOrWhiteSpace(ClientId) ||
        !string.IsNullOrWhiteSpace(ClientSecret);
}
