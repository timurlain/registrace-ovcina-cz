using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Email;

namespace RegistraceOvcina.Web.Tests;

public sealed class MailboxEmailSupportTests
{
    [Fact]
    public void MailboxOptions_RecognizeCompleteAndPartialConfiguration()
    {
        var configured = new MailboxEmailOptions
        {
            SharedMailboxAddress = "ovcina@ovcina.cz",
            Graph = new MicrosoftGraphOptions
            {
                TenantId = "tenant-id",
                ClientId = "client-id",
                ClientSecret = "client-secret"
            }
        };

        Assert.True(configured.IsConfigured);
        Assert.False(configured.HasPartialConfiguration);

        var partial = new MailboxEmailOptions
        {
            SharedMailboxAddress = "ovcina@ovcina.cz",
            Graph = new MicrosoftGraphOptions
            {
                TenantId = "tenant-id"
            }
        };

        Assert.False(partial.IsConfigured);
        Assert.True(partial.HasPartialConfiguration);
    }

    [Fact]
    public async Task MicrosoftGraphMailboxEmailSender_PostsSendMailRequestForSharedMailbox()
    {
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.Accepted);
        var clientFactory = new TestHttpClientFactory(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://graph.microsoft.com/v1.0/")
        });
        var sender = new MicrosoftGraphMailboxEmailSender(
            clientFactory,
            new StubGraphAccessTokenProvider("test-token"),
            Options.Create(new MailboxEmailOptions
            {
                SharedMailboxAddress = "ovcina@ovcina.cz",
                Graph = new MicrosoftGraphOptions
                {
                    TenantId = "tenant-id",
                    ClientId = "client-id",
                    ClientSecret = "client-secret"
                }
            }),
            NullLogger<MicrosoftGraphMailboxEmailSender>.Instance);

        await sender.SendConfirmationLinkAsync(
            new ApplicationUser(),
            "registrant@example.com",
            "https://example.com/confirm");

        Assert.Equal(HttpMethod.Post, handler.RequestMethod);
        Assert.Equal(
            "https://graph.microsoft.com/v1.0/users/ovcina%40ovcina.cz/sendMail",
            handler.RequestUri?.ToString());
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("test-token", handler.AuthorizationParameter);
        Assert.NotNull(handler.RequestBody);
        Assert.Contains("\"subject\":\"Confirm your email\"", handler.RequestBody);
        Assert.Contains("\"address\":\"registrant@example.com\"", handler.RequestBody);
        Assert.Contains("\"saveToSentItems\":true", handler.RequestBody);
    }

    [Fact]
    public async Task MicrosoftGraphMailboxEmailSender_ThrowsWhenGraphRejectsRequest()
    {
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.BadRequest, "{\"error\":\"denied\"}");
        var clientFactory = new TestHttpClientFactory(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://graph.microsoft.com/v1.0/")
        });
        var sender = new MicrosoftGraphMailboxEmailSender(
            clientFactory,
            new StubGraphAccessTokenProvider("test-token"),
            Options.Create(new MailboxEmailOptions
            {
                SharedMailboxAddress = "ovcina@ovcina.cz",
                Graph = new MicrosoftGraphOptions
                {
                    TenantId = "tenant-id",
                    ClientId = "client-id",
                    ClientSecret = "client-secret"
                }
            }),
            NullLogger<MicrosoftGraphMailboxEmailSender>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendPasswordResetCodeAsync(
                new ApplicationUser(),
                "registrant@example.com",
                "123456"));

        Assert.Contains("status code 400", exception.Message);
        Assert.Contains("denied", exception.Message);
    }

    private sealed class StubGraphAccessTokenProvider(string token) : IGraphAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHttpMessageHandler(HttpStatusCode statusCode, string responseBody = "{}") : HttpMessageHandler
    {
        public HttpMethod? RequestMethod { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? AuthorizationScheme { get; private set; }

        public string? AuthorizationParameter { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestMethod = request.Method;
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody)
            };
        }
    }
}
