namespace RegistraceOvcina.Web.Features.Feedback;

/// <summary>
/// Registered when <c>Email:SharedMailboxAddress</c> / Graph credentials are missing.
/// Throws on send so unconfigured environments surface the problem immediately instead
/// of appearing to work while mail silently never leaves the process. Mirrors
/// <c>UnconfiguredCharacterPrepEmailSender</c>.
/// </summary>
internal sealed class UnconfiguredFeedbackEmailSender(
    ILogger<UnconfiguredFeedbackEmailSender> logger) : IFeedbackEmailSender
{
    public Task SendAsync(string recipientEmail, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        logger.LogError(
            "Attempted to send Feedback email to {Recipient} but outbound mail is not configured " +
            "(Email:SharedMailboxAddress + Graph credentials). Configure them to enable sending.",
            recipientEmail);

        throw new InvalidOperationException(
            "Feedback email sending is not configured. Set Email:SharedMailboxAddress and the " +
            "Microsoft Graph credentials (Email:Graph:TenantId/ClientId/ClientSecret) to enable outbound mail.");
    }
}
