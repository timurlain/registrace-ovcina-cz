using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Email;

namespace RegistraceOvcina.Web.Features.Invitations;

public sealed record InvitationRecipientCandidate(
    string Email,
    string Name,
    string Source);

public sealed record SentInvitationSummary(
    int Id,
    string RecipientEmail,
    string RecipientName,
    string SentByUserId,
    DateTime SentAtUtc,
    string Subject);

public sealed record SendInvitationRequest(
    int GameId,
    IReadOnlyList<InvitationRecipientCandidate> Recipients,
    string Subject,
    string HtmlBody,
    string SentByUserId,
    string? Note);

public sealed class InvitationService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IHttpClientFactory httpClientFactory,
    IGraphAccessTokenProvider accessTokenProvider,
    IOptions<MailboxEmailOptions> emailOptions,
    ILogger<InvitationService> logger,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Returns all unique email addresses that have ever had an active registration
    /// for any game — suitable as a base pool for invitation targeting.
    /// </summary>
    public async Task<IReadOnlyList<InvitationRecipientCandidate>> GetCandidatesAsync(
        int gameId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // People who attended a previous game (have any active registration)
        var pastAttendees = await db.People
            .AsNoTracking()
            .Where(p => p.Registrations.Any(r =>
                r.Status == RegistrationStatus.Active &&
                r.Submission.GameId != gameId &&
                !r.Submission.IsDeleted))
            .Where(p => p.Email != null && p.Email != "")
            .Select(p => new { p.Email, p.FirstName, p.LastName })
            .Distinct()
            .ToListAsync(cancellationToken);

        // Registered users (have an account) who are not yet in past attendees
        var registeredUsers = await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive && u.Email != null && u.Email != "")
            .Select(u => new { u.Email, u.DisplayName })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<string, InvitationRecipientCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var attendee in pastAttendees)
        {
            var email = attendee.Email!;
            result[email] = new InvitationRecipientCandidate(
                email,
                $"{attendee.FirstName} {attendee.LastName}".Trim(),
                "Minulý účastník");
        }

        foreach (var user in registeredUsers)
        {
            var email = user.Email!;
            if (!result.ContainsKey(email))
            {
                result[email] = new InvitationRecipientCandidate(
                    email,
                    string.IsNullOrWhiteSpace(user.DisplayName) ? email : user.DisplayName,
                    "Registrovaný uživatel");
            }
        }

        return result.Values
            .OrderBy(r => r.Name)
            .ToList();
    }

    /// <summary>
    /// Sends invitations to the selected recipients via the shared mailbox and records each send.
    /// </summary>
    public async Task<int> SendInvitationsAsync(
        SendInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        var options = emailOptions.Value;
        if (!options.IsConfigured)
        {
            throw new InvalidOperationException(
                "Odesílání pozvánek vyžaduje nakonfigurovanou sdílenou schránku. " +
                MailboxEmailOptions.ValidationMessage);
        }

        var accessToken = await accessTokenProvider.GetAccessTokenAsync(cancellationToken);
        var httpClient = httpClientFactory.CreateClient(GraphHttpClientName);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var sentCount = 0;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        foreach (var recipient in request.Recipients)
        {
            await SendViaGraphAsync(
                httpClient,
                accessToken,
                options.SharedMailboxAddress!,
                recipient.Email,
                request.Subject,
                request.HtmlBody,
                cancellationToken);

            db.GameInvitations.Add(new GameInvitation
            {
                GameId = request.GameId,
                RecipientEmail = recipient.Email,
                RecipientName = recipient.Name,
                SentByUserId = request.SentByUserId,
                SentAtUtc = nowUtc,
                Subject = request.Subject,
                Note = request.Note
            });

            sentCount++;

            logger.LogInformation(
                "Invitation sent for game {GameId} to {RecipientEmail} from {SharedMailbox}",
                request.GameId, recipient.Email, options.SharedMailboxAddress);
        }

        await db.SaveChangesAsync(cancellationToken);

        return sentCount;
    }

    /// <summary>
    /// Returns the history of invitations already sent for a given game.
    /// </summary>
    public async Task<IReadOnlyList<SentInvitationSummary>> GetSentInvitationsAsync(
        int gameId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.GameInvitations
            .AsNoTracking()
            .Where(i => i.GameId == gameId)
            .OrderByDescending(i => i.SentAtUtc)
            .Select(i => new SentInvitationSummary(
                i.Id,
                i.RecipientEmail,
                i.RecipientName,
                i.SentByUserId,
                i.SentAtUtc,
                i.Subject))
            .ToListAsync(cancellationToken);
    }

    // ------------------------------------------------------------------ helpers

    internal const string GraphHttpClientName = "MicrosoftGraph";

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
