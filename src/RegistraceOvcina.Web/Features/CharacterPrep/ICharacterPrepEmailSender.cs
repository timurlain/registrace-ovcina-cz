namespace RegistraceOvcina.Web.Features.CharacterPrep;

/// <summary>
/// Thin seam over the actual mail transport. The only purpose of this interface is to let
/// tests swap in a capturing fake without touching the Microsoft Graph pipeline. The
/// production implementation (<see cref="GraphCharacterPrepEmailSender"/>) uses the same
/// Graph <c>sendMail</c> pattern as <c>InvitationService</c> / <c>InboxService</c>.
/// </summary>
public interface ICharacterPrepEmailSender
{
    Task SendAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken);
}
