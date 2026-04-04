using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace RegistraceOvcina.Web.Features.Email;

internal interface IGraphAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}

internal sealed class MicrosoftGraphAccessTokenProvider : IGraphAccessTokenProvider
{
    private static readonly string[] GraphScopes = ["https://graph.microsoft.com/.default"];

    private readonly ClientSecretCredential _credential;

    public MicrosoftGraphAccessTokenProvider(IOptions<MailboxEmailOptions> options)
    {
        var emailOptions = options.Value;

        if (!emailOptions.IsConfigured)
        {
            throw new InvalidOperationException(MailboxEmailOptions.ValidationMessage);
        }

        _credential = new ClientSecretCredential(
            emailOptions.Graph.TenantId!,
            emailOptions.Graph.ClientId!,
            emailOptions.Graph.ClientSecret!);
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(GraphScopes),
            cancellationToken);

        return token.Token;
    }
}
