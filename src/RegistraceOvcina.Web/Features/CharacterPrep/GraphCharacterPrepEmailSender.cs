using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Features.Email;

namespace RegistraceOvcina.Web.Features.CharacterPrep;

/// <summary>
/// Sends HTML mail through the shared mailbox via Microsoft Graph. Mirrors the exact pattern
/// already used by <c>InvitationService.SendViaGraphAsync</c> and <c>InboxService.SendNewMessageAsync</c>
/// so we don't fork the outbound pipeline. Only registered when <see cref="MailboxEmailOptions.IsConfigured"/>.
/// </summary>
public sealed class GraphCharacterPrepEmailSender(
    IHttpClientFactory httpClientFactory,
    IGraphAccessTokenProvider accessTokenProvider,
    IOptions<MailboxEmailOptions> emailOptions,
    ILogger<GraphCharacterPrepEmailSender> logger) : ICharacterPrepEmailSender
{
    public async Task SendAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        var options = emailOptions.Value;
        if (!options.IsConfigured)
        {
            throw new InvalidOperationException(
                "Character prep email sending requires Email:SharedMailboxAddress and Graph credentials.");
        }

        var sharedMailbox = options.SharedMailboxAddress!;
        var accessToken = await accessTokenProvider.GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"users/{Uri.EscapeDataString(sharedMailbox)}/sendMail");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            message = new
            {
                subject,
                body = new { contentType = "HTML", content = htmlBody },
                toRecipients = new[]
                {
                    new { emailAddress = new { address = recipientEmail } }
                }
            },
            saveToSentItems = true
        });

        using var client = httpClientFactory.CreateClient(MicrosoftGraphMailboxEmailSender.GraphHttpClientName);
        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Microsoft Graph sendMail failed ({(int)response.StatusCode}): {responseBody}");
        }

        logger.LogInformation(
            "Character prep email sent from {SharedMailbox} to {Recipient}", sharedMailbox, recipientEmail);
    }
}
