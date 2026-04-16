using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Email;

public sealed class InboxService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IOptions<MailboxEmailOptions> mailboxOptions,
    IHttpClientFactory? httpClientFactory = null,
    IGraphAccessTokenProvider? tokenProvider = null)
{
    public async Task<InboxPageResult> GetMessagesAsync(int page = 1, int pageSize = 25, EmailDirection? directionFilter = null)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var query = db.EmailMessages
            .Include(e => e.LinkedSubmission)
            .Include(e => e.LinkedPerson)
            .OrderByDescending(e => e.ReceivedAtUtc ?? e.SentAtUtc)
            .AsNoTracking();

        if (directionFilter.HasValue)
        {
            query = query.Where(e => e.Direction == directionFilter.Value);
        }

        var totalCount = await query.CountAsync();
        var messages = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // For inbound messages on this page, check if a related outbound reply exists
        var inboundIds = messages
            .Where(m => m.Direction == EmailDirection.Inbound)
            .Select(m => m.Id)
            .ToList();

        var repliedIds = new HashSet<int>();
        if (inboundIds.Count > 0)
        {
            // Find inbound messages that have a later outbound message to the same sender
            var inboundFromAddresses = messages
                .Where(m => m.Direction == EmailDirection.Inbound && !string.IsNullOrWhiteSpace(m.From))
                .Select(m => new { m.Id, From = m.From.Trim().ToLowerInvariant(), m.ReceivedAtUtc })
                .ToList();

            foreach (var inbound in inboundFromAddresses)
            {
                var hasReply = await db.EmailMessages
                    .AnyAsync(e =>
                        e.Direction == EmailDirection.Outbound
                        && e.To.ToLower() == inbound.From
                        && e.SentAtUtc > inbound.ReceivedAtUtc);

                if (hasReply)
                {
                    repliedIds.Add(inbound.Id);
                }
            }
        }

        return new InboxPageResult(messages, totalCount, page, pageSize, repliedIds);
    }

    public async Task<EmailMessage?> GetMessageAsync(int id)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        return await db.EmailMessages
            .Include(e => e.LinkedSubmission)
            .Include(e => e.LinkedPerson)
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task LinkToSubmissionAsync(int messageId, int submissionId, string actorUserId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var message = await db.EmailMessages.FindAsync(messageId);
        if (message is null) return;

        message.LinkedSubmissionId = submissionId;
        message.LinkedPersonId = null;

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(EmailMessage),
            EntityId = messageId.ToString(),
            Action = "LinkToSubmission",
            ActorUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow,
            DetailsJson = $"{{\"submissionId\":{submissionId}}}"
        });

        await db.SaveChangesAsync();
    }

    public async Task LinkToPersonAsync(int messageId, int personId, string actorUserId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var message = await db.EmailMessages.FindAsync(messageId);
        if (message is null) return;

        message.LinkedPersonId = personId;
        message.LinkedSubmissionId = null;

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(EmailMessage),
            EntityId = messageId.ToString(),
            Action = "LinkToPerson",
            ActorUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow,
            DetailsJson = $"{{\"personId\":{personId}}}"
        });

        await db.SaveChangesAsync();
    }

    public async Task UnlinkAsync(int messageId, string actorUserId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var message = await db.EmailMessages.FindAsync(messageId);
        if (message is null) return;

        message.LinkedSubmissionId = null;
        message.LinkedPersonId = null;

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(EmailMessage),
            EntityId = messageId.ToString(),
            Action = "Unlink",
            ActorUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    public async Task<List<SubmissionLookupItem>> GetSubmissionLookupAsync(int? gameId = null)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var query = db.RegistrationSubmissions
            .Where(s => s.Status == SubmissionStatus.Submitted);

        if (gameId.HasValue)
        {
            query = query.Where(s => s.GameId == gameId.Value);
        }

        return await query
            .OrderByDescending(s => s.SubmittedAtUtc)
            .Select(s => new SubmissionLookupItem(
                s.Id,
                $"#{s.Id} — {s.PrimaryContactName} ({s.Game.Name})"))
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<List<PersonLookupItem>> GetPersonLookupAsync(string? searchTerm = null)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var query = db.People.AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            query = query.Where(p =>
                (p.FirstName + " " + p.LastName).Contains(term)
                || (p.LastName + " " + p.FirstName).Contains(term)
                || (p.Email != null && p.Email.Contains(term)));
        }

        return await query
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .Take(50)
            .Select(p => new PersonLookupItem(
                p.Id,
                $"{p.LastName} {p.FirstName}" + (p.Email != null ? $" ({p.Email})" : "")))
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<int?> GetMostRecentGameIdAsync(CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        return await db.Games
            .AsNoTracking()
            .OrderByDescending(g => g.StartsAtUtc)
            .Select(g => (int?)g.Id)
            .FirstOrDefaultAsync(ct);
    }

    public bool CanReply => mailboxOptions.Value.IsConfigured
                            && httpClientFactory is not null
                            && tokenProvider is not null;

    public async Task<int> SendNewMessageAsync(
        string toEmail,
        string subject,
        string body,
        int? linkToPersonId,
        string actorUserId,
        CancellationToken ct = default)
    {
        var emailOptions = mailboxOptions.Value;
        if (!emailOptions.IsConfigured || httpClientFactory is null || tokenProvider is null)
        {
            throw new InvalidOperationException("Email is not configured. Cannot send messages.");
        }

        var sharedMailbox = emailOptions.SharedMailboxAddress!;

        // Send via Graph API
        var accessToken = await tokenProvider.GetAccessTokenAsync(ct);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"users/{Uri.EscapeDataString(sharedMailbox)}/sendMail");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            message = new
            {
                subject,
                body = new
                {
                    contentType = "Text",
                    content = body
                },
                toRecipients = new[]
                {
                    new
                    {
                        emailAddress = new
                        {
                            address = toEmail
                        }
                    }
                }
            },
            saveToSentItems = true
        });

        using var httpClient = httpClientFactory.CreateClient(MicrosoftGraphMailboxEmailSender.GraphHttpClientName);
        using var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Sending message through Microsoft Graph failed with status {(int)response.StatusCode}: {responseBody}");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // Auto-link to person by email if not explicitly provided
        int? resolvedPersonId = linkToPersonId;
        if (resolvedPersonId is null && !string.IsNullOrWhiteSpace(toEmail))
        {
            var normalizedToEmail = toEmail.Trim().ToLowerInvariant();
            resolvedPersonId = await db.People
                .Where(p => !p.IsDeleted && p.Email != null && p.Email.Trim().ToLower() == normalizedToEmail)
                .Select(p => (int?)p.Id)
                .FirstOrDefaultAsync(ct);
        }

        // Store the outbound message locally
        var newMessage = new EmailMessage
        {
            MailboxItemId = $"composed-{Guid.NewGuid()}",
            Direction = EmailDirection.Outbound,
            From = sharedMailbox,
            To = toEmail,
            Subject = subject,
            BodyText = body,
            SentAtUtc = DateTime.UtcNow,
            LinkedPersonId = resolvedPersonId
        };

        db.EmailMessages.Add(newMessage);
        await db.SaveChangesAsync(ct); // Save first so newMessage.Id is assigned

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(EmailMessage),
            EntityId = newMessage.Id.ToString(),
            Action = "NewMessageSent",
            ActorUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { recipient = toEmail, subject })
        });

        await db.SaveChangesAsync(ct);

        return newMessage.Id;
    }

    public async Task<int> SendReplyAsync(
        int originalMessageId,
        string replyBody,
        string actorUserId,
        CancellationToken ct = default)
    {
        var emailOptions = mailboxOptions.Value;
        if (!emailOptions.IsConfigured || httpClientFactory is null || tokenProvider is null)
        {
            throw new InvalidOperationException("Email is not configured. Cannot send replies.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var original = await db.EmailMessages.FindAsync([originalMessageId], ct)
            ?? throw new InvalidOperationException($"Message {originalMessageId} not found.");

        var recipientAddress = original.Direction == EmailDirection.Inbound
            ? original.From
            : original.To;

        if (string.IsNullOrWhiteSpace(recipientAddress))
        {
            throw new InvalidOperationException("Cannot determine recipient address for reply.");
        }

        var sharedMailbox = emailOptions.SharedMailboxAddress!;
        var replySubject = original.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
            ? original.Subject
            : $"Re: {original.Subject}";

        // Send via Graph API
        var accessToken = await tokenProvider.GetAccessTokenAsync(ct);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"users/{Uri.EscapeDataString(sharedMailbox)}/sendMail");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new
        {
            message = new
            {
                subject = replySubject,
                body = new
                {
                    contentType = "Text",
                    content = replyBody
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

        using var httpClient = httpClientFactory.CreateClient(MicrosoftGraphMailboxEmailSender.GraphHttpClientName);
        using var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Sending reply through Microsoft Graph failed with status {(int)response.StatusCode}: {responseBody}");
        }

        // Store the outbound message locally
        var replyMessage = new EmailMessage
        {
            MailboxItemId = $"reply-{originalMessageId}-{DateTime.UtcNow.Ticks}",
            Direction = EmailDirection.Outbound,
            From = sharedMailbox,
            To = recipientAddress,
            Subject = replySubject,
            BodyText = replyBody,
            SentAtUtc = DateTime.UtcNow,
            LinkedPersonId = original.LinkedPersonId,
            LinkedSubmissionId = original.LinkedSubmissionId
        };

        db.EmailMessages.Add(replyMessage);

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(EmailMessage),
            EntityId = originalMessageId.ToString(),
            Action = "ReplySent",
            ActorUserId = actorUserId,
            CreatedAtUtc = DateTime.UtcNow,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { replyTo = originalMessageId, recipient = recipientAddress })
        });

        await db.SaveChangesAsync(ct);

        return replyMessage.Id;
    }

    public async Task<List<GameLookupItem>> GetGamesForBulkEmailAsync(CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        return await db.Games
            .OrderByDescending(g => g.StartsAtUtc)
            .Select(g => new GameLookupItem(g.Id, g.Name))
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<List<string>> GetBulkRecipientsAsync(int? gameId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        if (gameId is { } gid)
        {
            // People registered for a specific game (via submissions)
            var submissionEmails = await db.RegistrationSubmissions
                .Where(s => s.GameId == gid && !s.IsDeleted && s.Status != SubmissionStatus.Cancelled)
                .Select(s => s.PrimaryEmail)
                .Distinct()
                .ToListAsync(ct);

            var registrationEmails = await db.Registrations
                .Where(r => r.Submission.GameId == gid && !r.Submission.IsDeleted
                    && r.Submission.Status != SubmissionStatus.Cancelled
                    && r.ContactEmail != null && r.ContactEmail != "")
                .Select(r => r.ContactEmail!)
                .Distinct()
                .ToListAsync(ct);

            return submissionEmails
                .Concat(registrationEmails)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim().ToLowerInvariant())
                .Distinct()
                .ToList();
        }

        // All contacts: people with email + submission primary emails
        var personEmails = await db.People
            .Where(p => !p.IsDeleted && p.Email != null && p.Email != "")
            .Select(p => p.Email!)
            .ToListAsync(ct);

        var allSubmissionEmails = await db.RegistrationSubmissions
            .Where(s => !s.IsDeleted && s.Status != SubmissionStatus.Cancelled && s.PrimaryEmail != "")
            .Select(s => s.PrimaryEmail)
            .ToListAsync(ct);

        return personEmails
            .Concat(allSubmissionEmails)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
    }

    public async Task<(int Sent, int Failed)> SendBulkEmailAsync(
        List<string> recipients,
        string subject,
        string body,
        string actorUserId,
        CancellationToken ct = default)
    {
        var sent = 0;
        var failed = 0;

        foreach (var email in recipients)
        {
            try
            {
                await SendNewMessageAsync(email, subject, body, null, actorUserId, ct);
                sent++;
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
            {
                failed++;
            }
        }

        return (sent, failed);
    }
}

public sealed record InboxPageResult(
    List<EmailMessage> Messages,
    int TotalCount,
    int Page,
    int PageSize,
    HashSet<int>? RepliedMessageIds = null)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
    public bool HasReply(int messageId) => RepliedMessageIds?.Contains(messageId) == true;
}

public sealed record SubmissionLookupItem(int Id, string Label);
public sealed record PersonLookupItem(int Id, string Name);
public sealed record GameLookupItem(int Id, string Name);
