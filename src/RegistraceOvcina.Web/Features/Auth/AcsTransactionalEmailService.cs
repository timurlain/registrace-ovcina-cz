using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Options;

namespace RegistraceOvcina.Web.Features.Auth;

public sealed partial class AcsTransactionalEmailService(
    IOptions<AcsEmailOptions> options,
    ILogger<AcsTransactionalEmailService> logger)
{
    private readonly object _clientLock = new();
    private EmailClient? _client;

    private EmailClient GetOrCreateClient(string connectionString)
    {
        if (_client is not null) return _client;
        lock (_clientLock)
        {
            return _client ??= new EmailClient(connectionString);
        }
    }

    public async Task SendMagicLinkAsync(string recipientEmail, string magicLinkUrl, CancellationToken ct = default)
    {
        var config = options.Value;
        logger.LogInformation(
            "ACS config check: IsConfigured={IsConfigured}, HasConnectionString={HasCs}, HasSender={HasSender}, Sender={Sender}",
            config.IsConfigured,
            !string.IsNullOrWhiteSpace(config.ConnectionString),
            !string.IsNullOrWhiteSpace(config.SenderAddress),
            config.SenderAddress ?? "(null)");

        if (!config.IsConfigured)
        {
            logger.LogWarning("ACS not configured — magic link NOT sent to {Email}", recipientEmail);
            return;
        }

        logger.LogInformation("Sending magic link to {Email} via ACS", recipientEmail);

        var client = GetOrCreateClient(config.ConnectionString!);

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

        var operation = await client.SendAsync(WaitUntil.Started, emailMessage, ct);
        logger.LogInformation(
            "ACS send accepted for {Email}: OperationId={OperationId}, HasCompleted={HasCompleted}",
            recipientEmail,
            operation.Id,
            operation.HasCompleted);
    }
}
