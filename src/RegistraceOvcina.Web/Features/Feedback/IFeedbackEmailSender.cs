namespace RegistraceOvcina.Web.Features.Feedback;

/// <summary>
/// Thin seam over the actual mail transport. Mirrors <c>ICharacterPrepEmailSender</c>:
/// the only purpose of this interface is to let tests swap in a capturing fake without
/// touching the Microsoft Graph pipeline. The production implementation
/// (<see cref="GraphFeedbackEmailSender"/>) uses the same Graph <c>sendMail</c> pattern as
/// <c>InvitationService</c> / <c>InboxService</c> / <c>GraphCharacterPrepEmailSender</c>.
/// </summary>
public interface IFeedbackEmailSender
{
    Task SendAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken);
}
