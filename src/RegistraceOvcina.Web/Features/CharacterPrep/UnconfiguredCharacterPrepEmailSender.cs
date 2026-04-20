namespace RegistraceOvcina.Web.Features.CharacterPrep;

/// <summary>
/// Registered when <c>Email:SharedMailboxAddress</c> / Graph credentials are missing.
/// Throws on send so unconfigured environments surface the problem immediately instead
/// of appearing to work while mail silently never leaves the process.
/// </summary>
internal sealed class UnconfiguredCharacterPrepEmailSender(
    ILogger<UnconfiguredCharacterPrepEmailSender> logger) : ICharacterPrepEmailSender
{
    public Task SendAsync(string recipientEmail, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        logger.LogError(
            "Attempted to send Character Prep email to {Recipient} but outbound mail is not configured " +
            "(Email:SharedMailboxAddress + Graph credentials). Configure them to enable sending.",
            recipientEmail);

        throw new InvalidOperationException(
            "Character Prep email sending is not configured. Set Email:SharedMailboxAddress and the " +
            "Microsoft Graph credentials (Email:Graph:TenantId/ClientId/ClientSecret) to enable outbound mail.");
    }
}
