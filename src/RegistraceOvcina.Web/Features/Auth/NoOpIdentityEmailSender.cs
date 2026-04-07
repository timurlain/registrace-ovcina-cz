using Microsoft.AspNetCore.Identity;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Auth;

/// <summary>
/// No-op email sender used when Microsoft Graph mailbox is not configured.
/// Identity requires IEmailSender registered for email change confirmation flows.
/// </summary>
internal sealed class NoOpIdentityEmailSender : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) => Task.CompletedTask;
    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) => Task.CompletedTask;
    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) => Task.CompletedTask;
}
