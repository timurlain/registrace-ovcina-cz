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

    // In-memory debounce for the auto-sync triggered on page load. Kept per
    // process — the sync button bypasses it by calling SyncInboxAsync directly,
    // and a process restart simply performs one fresh sync.
    private static DateTime s_lastSyncAtUtc = DateTime.MinValue;
    private static readonly TimeSpan AutoSyncCooldown = TimeSpan.FromMinutes(2);
    private static readonly object s_lastSyncLock = new();

    /// <summary>
    /// Runs <see cref="SyncInboxAsync"/> at most once per <see cref="AutoSyncCooldown"/>
    /// interval. Returns <c>true</c> when the sync actually ran, <c>false</c> when the
    /// cooldown short-circuited it. Used by the inbox page so opening it doesn't
    /// hammer Graph on every refresh.
    /// </summary>
    public async Task<bool> SyncIfStaleAsync(CancellationToken cancellationToken = default)
    {
        lock (s_lastSyncLock)
        {
            if (DateTime.UtcNow - s_lastSyncAtUtc < AutoSyncCooldown)
            {
                return false;
            }
            s_lastSyncAtUtc = DateTime.UtcNow;
        }

        try
        {
            await SyncInboxAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Auto-sync on inbox open failed; page will still render cached messages");
            // Roll the cooldown back so the user's manual retry isn't blocked
            lock (s_lastSyncLock) { s_lastSyncAtUtc = DateTime.MinValue; }
            return false;
        }
    }

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

        // Preload people emails for auto-linking (avoids N+1 queries)
        var peopleByEmail = await dbContext.People
            .Where(x => x.Email != null && !x.IsDeleted)
            .Select(x => new { x.Id, Email = x.Email!.ToLower() })
            .ToListAsync(cancellationToken);
        var emailToPersonId = peopleByEmail
            .GroupBy(x => x.Email)
            .ToDictionary(g => g.Key, g => g.First().Id);

        // Preload submission primary emails for auto-linking. Active (non-cancelled,
        // non-deleted) submissions for games that haven't ended yet win — stale
        // submissions don't shadow current ones.
        var submissionsByEmail = await dbContext.RegistrationSubmissions
            .Where(s => !s.IsDeleted
                && s.Status != SubmissionStatus.Cancelled
                && s.PrimaryEmail != ""
                && s.Game.EndsAtUtc >= DateTime.UtcNow.AddDays(-7))
            .OrderByDescending(s => s.Game.StartsAtUtc)
            .Select(s => new { s.Id, Email = s.PrimaryEmail.ToLower() })
            .ToListAsync(cancellationToken);
        var emailToSubmissionId = submissionsByEmail
            .GroupBy(x => x.Email)
            .ToDictionary(g => g.Key, g => g.First().Id);

        // Pre-load unreconciled local outbound rows (the ones created by InboxService
        // with a "composed-*" / "reply-*" placeholder id). Sync will try to "claim"
        // them by matching to+subject+body so Graph's next surfacing of the same
        // message doesn't create a duplicate row.
        var localPlaceholders = await dbContext.EmailMessages
            .Where(e => e.Direction == EmailDirection.Outbound
                && (e.MailboxItemId.StartsWith("composed-") || e.MailboxItemId.StartsWith("reply-")))
            .OrderByDescending(e => e.SentAtUtc)
            .ToListAsync(cancellationToken);

        var newCount = 0;
        var reconciledCount = 0;

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

            var isOutbound = !string.IsNullOrWhiteSpace(fromAddress)
                && fromAddress.Equals(sharedMailbox, StringComparison.OrdinalIgnoreCase);

            // Reconcile outbound messages with local placeholder rows from the
            // compose/reply flow. Without this step, Graph surfacing of the same
            // message on next sync creates a duplicate outbound row.
            if (isOutbound)
            {
                var subjectKey = msg.Subject ?? "";
                var normalizedTo = toAddresses.Trim().ToLowerInvariant();
                var match = localPlaceholders.FirstOrDefault(p =>
                    string.Equals(p.Subject, subjectKey, StringComparison.Ordinal)
                    && string.Equals(p.To.Trim().ToLowerInvariant(), normalizedTo, StringComparison.Ordinal));

                if (match is not null)
                {
                    match.MailboxItemId = msg.Id;
                    if (msg.ReceivedDateTime?.UtcDateTime is DateTime receivedUtc)
                    {
                        match.ReceivedAtUtc = receivedUtc;
                    }
                    localPlaceholders.Remove(match);
                    existingIds.Add(msg.Id);
                    reconciledCount++;
                    continue;
                }
            }

            var emailMessage = new EmailMessage
            {
                MailboxItemId = msg.Id,
                Direction = isOutbound ? EmailDirection.Outbound : EmailDirection.Inbound,
                From = fromAddress,
                To = toAddresses,
                Subject = msg.Subject ?? "",
                BodyText = bodyText,
                ReceivedAtUtc = msg.ReceivedDateTime?.UtcDateTime,
            };

            // Auto-link to submission/person. Submission wins when a current-game
            // PrimaryEmail matches; otherwise fall back to Person.Email.
            var linkAddress = isOutbound ? toAddresses.Split(';', StringSplitOptions.TrimEntries).FirstOrDefault() : fromAddress;
            if (!string.IsNullOrWhiteSpace(linkAddress))
            {
                var normalizedLink = linkAddress.Trim().ToLowerInvariant();
                if (emailToSubmissionId.TryGetValue(normalizedLink, out var submissionId))
                {
                    emailMessage.LinkedSubmissionId = submissionId;
                }
                else if (emailToPersonId.TryGetValue(normalizedLink, out var personId))
                {
                    emailMessage.LinkedPersonId = personId;
                }
            }

            dbContext.EmailMessages.Add(emailMessage);
            newCount++;
        }

        // Fix previously mistagged messages: any "Inbound" from the shared mailbox is actually Outbound
        var mistagged = await dbContext.EmailMessages
            .Where(e => e.Direction == EmailDirection.Inbound
                && e.From.ToLower() == sharedMailbox.ToLower())
            .ToListAsync(cancellationToken);

        foreach (var msg2 in mistagged)
        {
            msg2.Direction = EmailDirection.Outbound;
        }

        if (newCount > 0 || mistagged.Count > 0 || reconciledCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Mailbox sync complete: {NewCount} new, {FixedCount} direction-fixed, {ReconciledCount} outbound reconciled",
            newCount,
            mistagged.Count,
            reconciledCount);
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
