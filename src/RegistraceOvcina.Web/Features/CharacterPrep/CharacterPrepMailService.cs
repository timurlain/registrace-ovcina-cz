using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Email;

namespace RegistraceOvcina.Web.Features.CharacterPrep;

/// <summary>
/// Outcome of a single-submission send attempt. Explicit enum (not throw-on-failure) so
/// callers — especially the bulk organizer UI — can show per-row feedback without wrapping
/// every call in a try/catch. <see cref="Error"/> is used for unexpected exceptions AND
/// for the "reminder-before-invite" guard.
/// </summary>
public enum SendSingleResult
{
    Sent,
    AlreadyInvited,
    ThrottledSkipped,
    MissingRecipient,
    Error
}

public sealed record BulkSendResult(int Sent, int Failed, string? FirstError);

/// <summary>
/// Orchestrates the Character Prep outbound mail flow: token minting, rendering, Graph
/// dispatch, outbox logging via <see cref="EmailMessage"/>, and timestamp marking on the
/// submission. Single-send variants are idempotent and throttle-aware so the bulk variants
/// can simply iterate targets from the Phase 2 filter methods.
/// </summary>
public sealed class CharacterPrepMailService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ICharacterPrepEmailRenderer renderer,
    CharacterPrepTokenService tokenService,
    CharacterPrepService prepService,
    ICharacterPrepEmailSender sender,
    IOptions<CharacterPrepOptions> prepOptions,
    ILogger<CharacterPrepMailService> logger,
    IOptions<MailboxEmailOptions>? mailboxOptions = null)
{
    public async Task<SendSingleResult> SendPozvankaAsync(
        int submissionId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var context = await LoadContextAsync(submissionId, cancellationToken);
        if (context is null)
        {
            logger.LogWarning("SendPozvankaAsync: submission {SubmissionId} not found", submissionId);
            return SendSingleResult.Error;
        }

        if (string.IsNullOrWhiteSpace(context.Submission.PrimaryEmail))
        {
            return SendSingleResult.MissingRecipient;
        }

        if (context.Submission.CharacterPrepInvitedAtUtc is not null)
        {
            // Re-send is an explicit no-op so organizers get clear "already done" feedback
            // instead of accidentally spamming households.
            return SendSingleResult.AlreadyInvited;
        }

        try
        {
            var token = await tokenService.EnsureTokenAsync(context.Submission.Id, cancellationToken);
            var model = BuildEmailModel(context, token);
            var email = renderer.RenderPozvanka(model);

            await sender.SendAsync(
                context.Submission.PrimaryEmail,
                email.Subject,
                email.HtmlBody,
                cancellationToken);

            await LogOutboxAsync(
                submissionId: context.Submission.Id,
                to: context.Submission.PrimaryEmail,
                subject: email.Subject,
                bodyText: email.PlainTextBody,
                sentAtUtc: nowUtc,
                cancellationToken);

            await prepService.MarkInvitedAsync(context.Submission.Id, nowUtc, cancellationToken);

            return SendSingleResult.Sent;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendPozvankaAsync failed for submission {SubmissionId}", submissionId);
            return SendSingleResult.Error;
        }
    }

    public async Task<SendSingleResult> SendPripominkaAsync(
        int submissionId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var context = await LoadContextAsync(submissionId, cancellationToken);
        if (context is null)
        {
            logger.LogWarning("SendPripominkaAsync: submission {SubmissionId} not found", submissionId);
            return SendSingleResult.Error;
        }

        if (string.IsNullOrWhiteSpace(context.Submission.PrimaryEmail))
        {
            return SendSingleResult.MissingRecipient;
        }

        if (context.Submission.CharacterPrepInvitedAtUtc is null)
        {
            // Reminders only go to households that got a Pozvánka. Keeps the two flows
            // separate so organizers can't accidentally send a reminder as a first contact.
            logger.LogDebug(
                "SendPripominkaAsync: submission {SubmissionId} has no prior invite — refusing", submissionId);
            return SendSingleResult.Error;
        }

        var threshold = nowUtc.AddHours(-24);
        if (context.Submission.CharacterPrepReminderLastSentAtUtc is { } last && last >= threshold)
        {
            return SendSingleResult.ThrottledSkipped;
        }

        try
        {
            // Token must exist post-Pozvánka, but guard defensively if someone wipes it.
            var token = await tokenService.EnsureTokenAsync(context.Submission.Id, cancellationToken);
            var model = BuildEmailModel(context, token);
            var email = renderer.RenderPripominka(model);

            await sender.SendAsync(
                context.Submission.PrimaryEmail,
                email.Subject,
                email.HtmlBody,
                cancellationToken);

            await LogOutboxAsync(
                submissionId: context.Submission.Id,
                to: context.Submission.PrimaryEmail,
                subject: email.Subject,
                bodyText: email.PlainTextBody,
                sentAtUtc: nowUtc,
                cancellationToken);

            await prepService.MarkReminderSentAsync(context.Submission.Id, nowUtc, cancellationToken);

            return SendSingleResult.Sent;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendPripominkaAsync failed for submission {SubmissionId}", submissionId);
            return SendSingleResult.Error;
        }
    }

    public async Task<BulkSendResult> SendBulkPozvankaAsync(
        int gameId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var targets = await prepService.ListInvitationTargetsAsync(gameId, cancellationToken);
        return await RunBulkAsync(targets, submissionId =>
            SendPozvankaAsync(submissionId, nowUtc, cancellationToken), cancellationToken);
    }

    public async Task<BulkSendResult> SendBulkPripominkaAsync(
        int gameId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var targets = await prepService.ListReminderTargetsAsync(gameId, nowUtc, cancellationToken);
        return await RunBulkAsync(targets, submissionId =>
            SendPripominkaAsync(submissionId, nowUtc, cancellationToken), cancellationToken);
    }

    // ------------------------------------------------------------ helpers

    private async Task<BulkSendResult> RunBulkAsync(
        IReadOnlyList<RegistrationSubmission> targets,
        Func<int, Task<SendSingleResult>> sendOne,
        CancellationToken cancellationToken)
    {
        var sent = 0;
        var failed = 0;
        string? firstError = null;

        foreach (var submission in targets)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var result = await sendOne(submission.Id);
            switch (result)
            {
                case SendSingleResult.Sent:
                    sent++;
                    break;
                case SendSingleResult.Error:
                case SendSingleResult.MissingRecipient:
                    failed++;
                    firstError ??= $"Submission {submission.Id}: {result}";
                    break;
                case SendSingleResult.AlreadyInvited:
                case SendSingleResult.ThrottledSkipped:
                    // no-op; intentionally not counted as failure
                    break;
            }
        }

        return new BulkSendResult(sent, failed, firstError);
    }

    private async Task<LoadedContext?> LoadContextAsync(int submissionId, CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var submission = await db.RegistrationSubmissions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == submissionId, ct);
        if (submission is null)
        {
            return null;
        }

        var game = await db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == submission.GameId, ct);
        if (game is null)
        {
            return null;
        }

        var players = await db.Registrations
            .AsNoTracking()
            .Where(x => x.SubmissionId == submissionId && x.AttendeeType == AttendeeType.Player)
            .OrderBy(x => x.Person.LastName).ThenBy(x => x.Person.FirstName)
            .Select(x => x.Person.FirstName + " " + x.Person.LastName)
            .ToListAsync(ct);

        var options = await db.StartingEquipmentOptions
            .AsNoTracking()
            .Where(x => x.GameId == submission.GameId)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.DisplayName)
            .Select(x => new StartingEquipmentOptionView(
                x.Id, x.Key, x.DisplayName, x.Description, x.SortOrder))
            .ToListAsync(ct);

        return new LoadedContext(submission, game, players, options);
    }

    private CharacterPrepEmailModel BuildEmailModel(LoadedContext ctx, string token)
    {
        var options = prepOptions.Value;
        var baseUrl = (options.PublicBaseUrl ?? "").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "CharacterPrep:PublicBaseUrl is not configured; cannot build prep URL.");
        }

        var prepUrl = $"{baseUrl}/postavy/{token}";
        var organizerContact = options.OrganizerContactEmail
            ?? mailboxOptions?.Value.SharedMailboxAddress
            ?? "";

        return new CharacterPrepEmailModel(
            ctx.Game.Name,
            DateTime.SpecifyKind(ctx.Game.StartsAtUtc, DateTimeKind.Utc),
            prepUrl,
            ctx.PlayerNames,
            ctx.Options,
            organizerContact);
    }

    private async Task LogOutboxAsync(
        int submissionId,
        string to,
        string subject,
        string bodyText,
        DateTimeOffset sentAtUtc,
        CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        db.EmailMessages.Add(new EmailMessage
        {
            MailboxItemId = $"prep-{Guid.NewGuid()}",
            Direction = EmailDirection.Outbound,
            From = mailboxOptions?.Value.SharedMailboxAddress ?? "",
            To = to,
            Subject = subject,
            BodyText = bodyText,
            SentAtUtc = sentAtUtc.UtcDateTime,
            LinkedSubmissionId = submissionId
        });
        await db.SaveChangesAsync(ct);
    }

    private sealed record LoadedContext(
        RegistrationSubmission Submission,
        Game Game,
        IReadOnlyList<string> PlayerNames,
        IReadOnlyList<StartingEquipmentOptionView> Options);
}
