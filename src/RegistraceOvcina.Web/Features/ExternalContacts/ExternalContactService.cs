using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Email;

namespace RegistraceOvcina.Web.Features.ExternalContacts;

public sealed class ExternalContactService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IHttpClientFactory httpClientFactory,
    IGraphAccessTokenProvider accessTokenProvider,
    IOptions<MailboxEmailOptions> emailOptions,
    ILogger<ExternalContactService> logger,
    TimeProvider timeProvider)
{
    internal const string GraphHttpClientName = "MicrosoftGraph";

    /// <summary>
    /// Imports a collection of email addresses as external contacts.
    /// Normalizes, deduplicates, and skips already-existing entries.
    /// </summary>
    public async Task<(int Added, int Skipped)> ImportAsync(
        IEnumerable<string> emails,
        CancellationToken cancellationToken = default)
    {
        var normalized = emails
            .Select(e => e?.Trim().ToLowerInvariant() ?? "")
            .Where(e => !string.IsNullOrWhiteSpace(e) && e.Contains('@'))
            .Distinct()
            .ToList();

        if (normalized.Count == 0)
            return (0, 0);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await db.ExternalContacts
            .Where(c => normalized.Contains(c.Email))
            .Select(c => c.Email)
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var added = 0;

        foreach (var email in normalized)
        {
            if (existingSet.Contains(email))
                continue;

            db.ExternalContacts.Add(new ExternalContact
            {
                Email = email,
                CreatedAtUtc = nowUtc
            });

            existingSet.Add(email);
            added++;
        }

        if (added > 0)
            await db.SaveChangesAsync(cancellationToken);

        var skipped = normalized.Count - added;

        logger.LogInformation(
            "External contact import: {Added} added, {Skipped} skipped (already existed or duplicate)",
            added, skipped);

        return (added, skipped);
    }

    /// <summary>
    /// Returns all external contacts ordered by most recently created first.
    /// </summary>
    public async Task<List<ExternalContact>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.ExternalContacts
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes a single external contact by ID.
    /// </summary>
    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        await db.ExternalContacts
            .Where(c => c.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Sends an email to all external contacts via the shared mailbox.
    /// Returns the number of emails successfully sent.
    /// </summary>
    public async Task<int> SendToAllAsync(
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        var options = emailOptions.Value;
        if (!options.IsConfigured)
        {
            logger.LogWarning(
                "Cannot send to external contacts: shared mailbox is not configured. {Message}",
                MailboxEmailOptions.ValidationMessage);
            return 0;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var contactEmails = await db.ExternalContacts
            .AsNoTracking()
            .Select(c => c.Email)
            .ToListAsync(cancellationToken);

        if (contactEmails.Count == 0)
        {
            logger.LogInformation("No external contacts to send to");
            return 0;
        }

        var accessToken = await accessTokenProvider.GetAccessTokenAsync(cancellationToken);
        var httpClient = httpClientFactory.CreateClient(GraphHttpClientName);
        var sentCount = 0;

        foreach (var email in contactEmails)
        {
            try
            {
                await SendViaGraphAsync(
                    httpClient,
                    accessToken,
                    options.SharedMailboxAddress!,
                    email,
                    subject,
                    htmlBody,
                    cancellationToken);

                sentCount++;

                logger.LogInformation(
                    "External contact email sent to {RecipientEmail} from {SharedMailbox}",
                    email, options.SharedMailboxAddress);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to send external contact email to {RecipientEmail}",
                    email);
            }
        }

        logger.LogInformation(
            "External contact send complete: {SentCount}/{TotalCount} emails sent",
            sentCount, contactEmails.Count);

        return sentCount;
    }

    // ------------------------------------------------------------------ helpers

    private static async Task SendViaGraphAsync(
        HttpClient client,
        string accessToken,
        string fromMailbox,
        string recipientAddress,
        string subject,
        string htmlContent,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"users/{Uri.EscapeDataString(fromMailbox)}/sendMail");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            message = new
            {
                subject,
                body = new { contentType = "HTML", content = htmlContent },
                toRecipients = new[]
                {
                    new { emailAddress = new { address = recipientAddress } }
                }
            },
            saveToSentItems = true
        });

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Microsoft Graph sendMail failed ({(int)response.StatusCode}): {body}");
        }
    }
}
