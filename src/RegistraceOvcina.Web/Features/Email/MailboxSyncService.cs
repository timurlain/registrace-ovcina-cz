using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Email;

internal sealed partial class MailboxSyncService(
    IHttpClientFactory httpClientFactory,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IGraphAccessTokenProvider tokenProvider,
    IOptions<MailboxEmailOptions> options,
    ILogger<MailboxSyncService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task SyncInboxAsync(CancellationToken cancellationToken = default)
    {
        var emailOptions = options.Value;

        if (!emailOptions.IsConfigured)
        {
            logger.LogWarning("Mailbox sync skipped — email is not configured");
            return;
        }

        var accessToken = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        var sharedMailbox = emailOptions.SharedMailboxAddress!;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"users/{Uri.EscapeDataString(sharedMailbox)}/messages" +
            "?$select=id,subject,from,toRecipients,receivedDateTime,body,hasAttachments" +
            "&$orderby=receivedDateTime desc&$top=50");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var httpClient = httpClientFactory.CreateClient(MicrosoftGraphMailboxEmailSender.GraphHttpClientName);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError(
                "Mailbox sync failed with status {StatusCode}: {ResponseBody}",
                (int)response.StatusCode,
                responseBody);
            return;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var graphResponse = JsonSerializer.Deserialize<GraphMessagesResponse>(json, JsonOptions);

        if (graphResponse?.Value is null or { Count: 0 })
        {
            logger.LogInformation("Mailbox sync: no messages returned from Graph API");
            return;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existingIds = await dbContext.EmailMessages
            .Select(e => e.MailboxItemId)
            .ToHashSetAsync(cancellationToken);

        var newCount = 0;

        foreach (var msg in graphResponse.Value)
        {
            if (string.IsNullOrEmpty(msg.Id) || existingIds.Contains(msg.Id))
            {
                continue;
            }

            var fromAddress = msg.From?.EmailAddress?.Address ?? "";
            var toAddresses = msg.ToRecipients is { Count: > 0 }
                ? string.Join("; ", msg.ToRecipients.Select(r => r.EmailAddress?.Address ?? ""))
                : "";

            var bodyText = msg.Body?.ContentType?.Equals("text", StringComparison.OrdinalIgnoreCase) == true
                ? msg.Body.Content ?? ""
                : StripHtml(msg.Body?.Content ?? "");

            if (bodyText.Length > 20000)
            {
                bodyText = bodyText[..20000];
            }

            var emailMessage = new EmailMessage
            {
                MailboxItemId = msg.Id,
                Direction = EmailDirection.Inbound,
                From = fromAddress,
                To = toAddresses,
                Subject = msg.Subject ?? "",
                BodyText = bodyText,
                ReceivedAtUtc = msg.ReceivedDateTime?.UtcDateTime,
            };

            dbContext.EmailMessages.Add(emailMessage);
            newCount++;
        }

        if (newCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Mailbox sync complete: {NewCount} new messages synced", newCount);
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return "";
        }

        // Remove style and script blocks
        var cleaned = StyleOrScriptRegex().Replace(html, " ");
        // Replace <br> and block-level tags with newlines
        cleaned = BlockTagRegex().Replace(cleaned, "\n");
        // Strip remaining HTML tags
        cleaned = HtmlTagRegex().Replace(cleaned, "");
        // Decode common HTML entities
        cleaned = cleaned
            .Replace("&nbsp;", " ")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'");
        // Collapse whitespace
        cleaned = ExcessiveWhitespaceRegex().Replace(cleaned, " ");
        cleaned = ExcessiveNewlinesRegex().Replace(cleaned, "\n\n");

        return cleaned.Trim();
    }

    [GeneratedRegex(@"<(style|script)[^>]*>[\s\S]*?</\1>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleOrScriptRegex();

    [GeneratedRegex(@"<(br|p|div|tr|li|h[1-6])[^>]*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex ExcessiveWhitespaceRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessiveNewlinesRegex();

    // Graph API response DTOs

    private sealed class GraphMessagesResponse
    {
        public List<GraphMessage>? Value { get; set; }
    }

    private sealed class GraphMessage
    {
        public string? Id { get; set; }
        public string? Subject { get; set; }
        public GraphRecipient? From { get; set; }
        public List<GraphRecipient>? ToRecipients { get; set; }
        public DateTimeOffset? ReceivedDateTime { get; set; }
        public GraphBody? Body { get; set; }
        public bool HasAttachments { get; set; }
    }

    private sealed class GraphRecipient
    {
        public GraphEmailAddress? EmailAddress { get; set; }
    }

    private sealed class GraphEmailAddress
    {
        public string? Address { get; set; }
        public string? Name { get; set; }
    }

    private sealed class GraphBody
    {
        public string? ContentType { get; set; }
        public string? Content { get; set; }
    }
}
