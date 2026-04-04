using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Email;

internal sealed class MicrosoftGraphMailboxEmailSender : IEmailSender<ApplicationUser>
{
    internal const string GraphHttpClientName = "MicrosoftGraph";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGraphAccessTokenProvider _accessTokenProvider;
    private readonly ILogger<MicrosoftGraphMailboxEmailSender> _logger;
    private readonly string _sharedMailboxAddress;

    public MicrosoftGraphMailboxEmailSender(
        IHttpClientFactory httpClientFactory,
        IGraphAccessTokenProvider accessTokenProvider,
        IOptions<MailboxEmailOptions> options,
        ILogger<MicrosoftGraphMailboxEmailSender> logger)
    {
        var emailOptions = options.Value;

        if (!emailOptions.IsConfigured)
        {
            throw new InvalidOperationException(MailboxEmailOptions.ValidationMessage);
        }

        _httpClientFactory = httpClientFactory;
        _accessTokenProvider = accessTokenProvider;
        _logger = logger;
        _sharedMailboxAddress = emailOptions.SharedMailboxAddress!;
    }

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        SendEmailAsync(
            email,
            "Confirm your email",
            $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(confirmationLink)}'>clicking here</a>.");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendEmailAsync(
            email,
            "Reset your password",
            $"Please reset your password by <a href='{HtmlEncoder.Default.Encode(resetLink)}'>clicking here</a>.");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        SendEmailAsync(
            email,
            "Reset your password",
            $"Please reset your password using the following code: {HtmlEncoder.Default.Encode(resetCode)}");

    private async Task SendEmailAsync(string recipientAddress, string subject, string htmlContent)
    {
        var accessToken = await _accessTokenProvider.GetAccessTokenAsync(CancellationToken.None);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"users/{Uri.EscapeDataString(_sharedMailboxAddress)}/sendMail");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            message = new
            {
                subject,
                body = new
                {
                    contentType = "HTML",
                    content = htmlContent
                },
                toRecipients = new[]
                {
                    new
                    {
                        emailAddress = new
                        {
                            address = recipientAddress
                        }
                    }
                }
            },
            saveToSentItems = true
        });

        using var response = await _httpClientFactory
            .CreateClient(GraphHttpClientName)
            .SendAsync(request, CancellationToken.None);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(CancellationToken.None);

            throw new InvalidOperationException(
                $"Sending email through Microsoft Graph failed with status code {(int)response.StatusCode}: {responseBody}");
        }

        _logger.LogInformation(
            "Sent email through Microsoft Graph from {SharedMailboxAddress} to {RecipientAddress}",
            _sharedMailboxAddress,
            recipientAddress);
    }
}
