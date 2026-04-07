using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Options;

namespace RegistraceOvcina.Web.Features.Auth;

public sealed partial class AcsTransactionalEmailService(
    IOptions<AcsEmailOptions> options,
    ILogger<AcsTransactionalEmailService> logger)
{
    public async Task SendMagicLinkAsync(string recipientEmail, string magicLinkUrl, CancellationToken ct = default)
    {
        var config = options.Value;
        if (!config.IsConfigured)
        {
            logger.LogWarning("ACS not configured — magic link NOT sent to {Email}", recipientEmail);
            return;
        }

        logger.LogInformation("Sending magic link to {Email} via ACS", recipientEmail);

        var client = new EmailClient(config.ConnectionString);

        var emailMessage = new EmailMessage(
            senderAddress: config.SenderAddress,
            recipientAddress: recipientEmail,
            content: new EmailContent("Přihlášení — Ovčina registrace")
            {
                PlainText = $"""
                    Dobrý den,

                    Pro přihlášení do registrace Ovčina klikněte na následující odkaz:

                    {magicLinkUrl}

                    Odkaz je platný 60 minut. Pokud jste o přihlášení nežádali, tento e-mail ignorujte.

                    Ovčina registrace
                    """
            });

        await client.SendAsync(WaitUntil.Started, emailMessage, ct);
        logger.LogInformation("ACS send accepted for {Email}", recipientEmail);
    }
}
