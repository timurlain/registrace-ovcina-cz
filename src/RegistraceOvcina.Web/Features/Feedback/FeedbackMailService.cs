using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Feedback;

/// <summary>
/// Outcome of a bulk send attempt. Counts every Graph <c>sendMail</c> call we
/// successfully made (split by bundle vs individual envelope), plus a count
/// of submissions whose dispatch failed for any reason — missing contact email
/// when a bundle was needed, sender exception, classifier error. Mirrors the
/// shape of <see cref="CharacterPrep.BulkSendResult"/> but is per-email-shaped
/// because Feedback fans one submission out into 0-N envelopes.
/// </summary>
public sealed record FeedbackBulkSendResult(
    int BundleEmailsSent,
    int IndividualEmailsSent,
    int ErrorsLogged);

/// <summary>
/// Orchestrates post-game feedback outbound mail: pulls invitation/reminder
/// targets from <see cref="FeedbackService"/>, ensures every targeted
/// Registration has a token via <see cref="FeedbackTokenService"/>, classifies
/// each submission with <see cref="FeedbackEmailRouter"/> into one optional
/// bundle + 0..N individual adult emails, renders via
/// <see cref="IFeedbackEmailRenderer"/>, dispatches via
/// <see cref="IFeedbackEmailSender"/>, and stamps
/// <see cref="Registration.FeedbackInvitedAtUtc"/> /
/// <see cref="Registration.FeedbackReminderLastSentAtUtc"/> only after each
/// successful send.
/// </summary>
/// <remarks>
/// <para>Per-submission errors are logged and counted but never abort the
/// rest of the bulk run — organizers can re-run the bulk button after fixing
/// the offending row. <see cref="OperationCanceledException"/> is the only
/// exception that propagates so shutdown / cancel still work.</para>
/// <para>Token issuance always goes through
/// <see cref="FeedbackTokenService.EnsureTokenAsync"/> rather than touching
/// the DB directly, so token semantics (lazy issue, never rotate) stay in one
/// place. The router receives a callback that maps a Registration to its
/// final URL using <see cref="FeedbackOptions.PublicBaseUrl"/>.</para>
/// </remarks>
public sealed class FeedbackMailService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    FeedbackService feedbackService,
    FeedbackTokenService tokenService,
    IFeedbackEmailRenderer emailRenderer,
    IFeedbackEmailSender emailSender,
    IOptions<FeedbackOptions> feedbackOptions,
    TimeProvider timeProvider,
    ILogger<FeedbackMailService> logger)
{
    public Task<FeedbackBulkSendResult> SendInvitationsAsync(int gameId, CancellationToken ct)
        => SendBulkAsync(gameId, isReminder: false, ct);

    public Task<FeedbackBulkSendResult> SendRemindersAsync(int gameId, CancellationToken ct)
        => SendBulkAsync(gameId, isReminder: true, ct);

    // ------------------------------------------------------------ bulk core

    private async Task<FeedbackBulkSendResult> SendBulkAsync(
        int gameId,
        bool isReminder,
        CancellationToken ct)
    {
        var nowUtc = timeProvider.GetUtcNow();

        var targets = isReminder
            ? await feedbackService.ListReminderTargetsAsync(gameId, nowUtc, ct)
            : await feedbackService.ListInvitationTargetsAsync(gameId, ct);

        if (targets.Count == 0)
        {
            return new FeedbackBulkSendResult(0, 0, 0);
        }

        // Targets come back AsNoTracking() and grouped flat. We need the
        // submission + game + person for every row, so re-load them with the
        // navigation properties populated. One query keyed by submission id
        // keeps the round-trip count small.
        var submissionIds = targets.Select(r => r.SubmissionId).Distinct().ToList();

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var submissions = await db.RegistrationSubmissions
            .AsNoTracking()
            .Where(s => submissionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        var game = await db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == gameId, ct);
        if (game is null)
        {
            // No game means no work; this is a misuse rather than a row-level
            // error so we surface it as a single error count + log.
            logger.LogWarning(
                "FeedbackMailService: game {GameId} not found; bulk send skipped.", gameId);
            return new FeedbackBulkSendResult(0, 0, 1);
        }

        // Re-fetch targeted Registrations WITH Person + Submission included so
        // the router has the email + name fields it needs. We trust
        // ListInvitationTargets / ListReminderTargets for membership and just
        // use the ids here.
        var targetIds = targets.Select(r => r.Id).ToList();
        var registrations = await db.Registrations
            .AsNoTracking()
            .Include(r => r.Person)
            .Include(r => r.Submission)
            .Where(r => targetIds.Contains(r.Id))
            .ToListAsync(ct);

        var grouped = registrations
            .GroupBy(r => r.SubmissionId)
            .OrderBy(g => g.Key); // stable iteration order for deterministic tests

        var bundleSent = 0;
        var individualSent = 0;
        var errors = 0;

        foreach (var group in grouped)
        {
            ct.ThrowIfCancellationRequested();

            if (!submissions.TryGetValue(group.Key, out var submission))
            {
                logger.LogWarning(
                    "FeedbackMailService: submission {SubmissionId} disappeared between target lookup and dispatch.",
                    group.Key);
                errors++;
                continue;
            }

            try
            {
                var (sentBundle, sentIndividuals) = await DispatchSubmissionAsync(
                    submission, game, group.ToList(), isReminder, nowUtc, ct);

                if (sentBundle) bundleSent++;
                individualSent += sentIndividuals;
            }
            catch (OperationCanceledException)
            {
                // Honour cancellation — never swallow.
                throw;
            }
            catch (Exception ex)
            {
                // Per-submission failures are logged + counted; we keep going
                // so a single bad row doesn't strand the rest of the bulk.
                logger.LogError(
                    ex,
                    "FeedbackMailService: dispatch failed for submission {SubmissionId} (game {GameId}, isReminder={IsReminder})",
                    group.Key, gameId, isReminder);
                errors++;
            }
        }

        return new FeedbackBulkSendResult(bundleSent, individualSent, errors);
    }

    // ----------------------------------------------------- per-submission step

    private async Task<(bool BundleSent, int IndividualSent)> DispatchSubmissionAsync(
        RegistrationSubmission submission,
        Game game,
        IReadOnlyList<Registration> registrations,
        bool isReminder,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        // Step 1: ensure every targeted registration has a token before we
        // hand them to the router (router builds URLs synchronously).
        var tokens = new Dictionary<int, Guid>(registrations.Count);
        foreach (var reg in registrations)
        {
            ct.ThrowIfCancellationRequested();
            tokens[reg.Id] = await tokenService.EnsureTokenAsync(reg.Id, ct);
        }

        // Step 2: pre-flight bundle requirement. The router throws if a
        // bundle is needed but PrimaryEmail is blank; we'd rather log + count
        // that as a per-submission error than let it bubble out as an
        // exception that aborts the whole bulk run.
        var needsBundle = registrations.Any(r =>
            r.AttendeeType == AttendeeType.Player
            || string.IsNullOrWhiteSpace(r.Person?.Email));
        if (needsBundle && string.IsNullOrWhiteSpace(submission.PrimaryEmail))
        {
            logger.LogWarning(
                "FeedbackMailService: submission {SubmissionId} has no PrimaryEmail but requires a bundle envelope; skipping.",
                submission.Id);
            // Throwing here lets the outer catch increment the error counter
            // without us double-bookkeeping the count.
            throw new InvalidOperationException(
                $"Submission {submission.Id} requires a bundle but has no PrimaryEmail.");
        }

        // Step 3: classify into bundle + individuals.
        var (bundle, individuals) = FeedbackEmailRouter.Classify(
            submission,
            registrations,
            game,
            r => BuildTokenLink(tokens[r.Id]),
            isReminder);

        // Step 3b: enrich with per-game template overrides where present. The
        // renderer falls back to the canonical default body whenever a slot is
        // null/blank, so an unset template is invisible.
        if (bundle is not null
            && (!string.IsNullOrWhiteSpace(game.FeedbackBundleSubjectTemplate)
                || !string.IsNullOrWhiteSpace(game.FeedbackBundleHtmlTemplate)))
        {
            bundle = bundle with
            {
                SubjectTemplate = game.FeedbackBundleSubjectTemplate,
                HtmlTemplate = game.FeedbackBundleHtmlTemplate,
            };
        }

        if (individuals.Count > 0
            && (!string.IsNullOrWhiteSpace(game.FeedbackAdultIndividualSubjectTemplate)
                || !string.IsNullOrWhiteSpace(game.FeedbackAdultIndividualHtmlTemplate)))
        {
            individuals = individuals
                .Select(m => m with
                {
                    SubjectTemplate = game.FeedbackAdultIndividualSubjectTemplate,
                    HtmlTemplate = game.FeedbackAdultIndividualHtmlTemplate,
                })
                .ToList();
        }

        var bundleSent = false;
        var individualSent = 0;

        // Step 4: send the bundle (if any). Mark contained registrations
        // ONLY after the send succeeds.
        if (bundle is not null)
        {
            var rendered = emailRenderer.RenderContactBundle(bundle);
            await emailSender.SendAsync(
                bundle.ToEmail, rendered.Subject, rendered.HtmlBody, ct);

            // Bundle entries map back to registrations by attendee name +
            // type. We lean on the fact the router preserves order, so we can
            // filter the original list rather than do a fragile name match.
            var bundledRegistrations = registrations
                .Where(r => r.AttendeeType == AttendeeType.Player
                    || string.IsNullOrWhiteSpace(r.Person?.Email))
                .ToList();

            await MarkAsync(bundledRegistrations, isReminder, nowUtc, ct);
            bundleSent = true;
        }

        // Step 5: send individuals. Each adult-with-email is its own envelope
        // so a single bad address only affects that one row.
        if (individuals.Count > 0)
        {
            var individualRegistrations = registrations
                .Where(r => r.AttendeeType != AttendeeType.Player
                    && !string.IsNullOrWhiteSpace(r.Person?.Email))
                .ToList();

            // The router preserves input order so the lists line up. We zip
            // them rather than re-matching by email so duplicate addresses
            // (rare but possible) don't get crossed.
            for (var i = 0; i < individuals.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var model = individuals[i];
                var rendered = emailRenderer.RenderAdultIndividual(model);
                await emailSender.SendAsync(
                    model.ToEmail, rendered.Subject, rendered.HtmlBody, ct);

                if (i < individualRegistrations.Count)
                {
                    await MarkAsync(
                        new[] { individualRegistrations[i] }, isReminder, nowUtc, ct);
                }
                individualSent++;
            }
        }

        return (bundleSent, individualSent);
    }

    private async Task MarkAsync(
        IReadOnlyList<Registration> registrations,
        bool isReminder,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        foreach (var reg in registrations)
        {
            if (isReminder)
            {
                await feedbackService.MarkReminderSentAsync(reg.Id, nowUtc, ct);
            }
            else
            {
                await feedbackService.MarkInvitedAsync(reg.Id, nowUtc, ct);
            }
        }
    }

    private string BuildTokenLink(Guid token)
    {
        var options = feedbackOptions.Value;
        var baseUrl = (options.PublicBaseUrl ?? "").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "Feedback:PublicBaseUrl is not configured; cannot build feedback URL.");
        }

        return $"{baseUrl}/zpetna-vazba/{token}";
    }

    // -------------------------------------------------------------------------
    // Test-send: organizer "send to me" preview from the template editor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Renders + sends a sample bundle (household-contact) feedback email to
    /// <paramref name="toEmail"/> using the same renderer + sender + per-game
    /// template overrides as the real bulk send. The link targets in the body
    /// are randomly generated GUIDs — they do NOT resolve to any real
    /// Registration. Used by the organizer template editor's "send a test to
    /// me" buttons so the organizer sees exactly what real recipients will get
    /// before triggering the bulk send.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="toEmail"/> is null/blank, when the game
    /// does not exist, or when <see cref="FeedbackOptions.PublicBaseUrl"/> is
    /// unconfigured (mirrors the real-send safeguard).
    /// </exception>
    public async Task SendTestBundleAsync(
        int gameId,
        string toEmail,
        bool isReminder,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            throw new InvalidOperationException("Cieľová e-mailová adresa je prázdná.");
        }

        var options = feedbackOptions.Value;
        var baseUrl = (options.PublicBaseUrl ?? "").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "Feedback:PublicBaseUrl is not configured; cannot build feedback URL.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var game = await db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == gameId, ct)
            ?? throw new InvalidOperationException($"Game {gameId} not found.");

        // Mirror FeedbackService.ComputeWindow: closes-at falls back to
        // EndsAtUtc + 30 days when not configured. Keeps the rendered deadline
        // string consistent with whatever the organizer sees on the dashboard.
        var endsAt = new DateTimeOffset(DateTime.SpecifyKind(game.EndsAtUtc, DateTimeKind.Utc));
        var closesAt = game.FeedbackClosesAtUtc ?? endsAt.AddDays(30);

        var sample = new FeedbackContactBundleEmail(
            ToEmail: toEmail,
            ContactName: "Testovací rodič",
            GameName: game.Name,
            FeedbackClosesAtLocal: closesAt,
            Entries: new List<FeedbackBundleEntry>
            {
                new("Anička (test)", AttendeeType.Player,
                    $"{baseUrl}/zpetna-vazba/{Guid.NewGuid()}"),
                new("Kuba (test)", AttendeeType.Player,
                    $"{baseUrl}/zpetna-vazba/{Guid.NewGuid()}"),
            },
            IsReminder: isReminder,
            SubjectTemplate: game.FeedbackBundleSubjectTemplate,
            HtmlTemplate: game.FeedbackBundleHtmlTemplate);

        var rendered = emailRenderer.RenderContactBundle(sample);
        await emailSender.SendAsync(toEmail, rendered.Subject, rendered.HtmlBody, ct);

        logger.LogInformation(
            "FeedbackMailService: test bundle email sent to {ToEmail} (game {GameId}, isReminder={IsReminder}).",
            toEmail, gameId, isReminder);
    }

    /// <summary>
    /// Renders + sends a sample adult-individual feedback email to
    /// <paramref name="toEmail"/> using the same renderer + sender + per-game
    /// template overrides as the real bulk send. The link target is a random
    /// GUID — it does NOT resolve to any real Registration.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="toEmail"/> is null/blank, when the game
    /// does not exist, or when <see cref="FeedbackOptions.PublicBaseUrl"/> is
    /// unconfigured.
    /// </exception>
    public async Task SendTestAdultIndividualAsync(
        int gameId,
        string toEmail,
        bool isReminder,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            throw new InvalidOperationException("Cieľová e-mailová adresa je prázdná.");
        }

        var options = feedbackOptions.Value;
        var baseUrl = (options.PublicBaseUrl ?? "").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "Feedback:PublicBaseUrl is not configured; cannot build feedback URL.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var game = await db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == gameId, ct)
            ?? throw new InvalidOperationException($"Game {gameId} not found.");

        var endsAt = new DateTimeOffset(DateTime.SpecifyKind(game.EndsAtUtc, DateTimeKind.Utc));
        var closesAt = game.FeedbackClosesAtUtc ?? endsAt.AddDays(30);

        var sample = new FeedbackAdultIndividualEmail(
            ToEmail: toEmail,
            AttendeeName: "Testovací dospělý",
            GameName: game.Name,
            FeedbackClosesAtLocal: closesAt,
            TokenLink: $"{baseUrl}/zpetna-vazba/{Guid.NewGuid()}",
            IsReminder: isReminder,
            SubjectTemplate: game.FeedbackAdultIndividualSubjectTemplate,
            HtmlTemplate: game.FeedbackAdultIndividualHtmlTemplate);

        var rendered = emailRenderer.RenderAdultIndividual(sample);
        await emailSender.SendAsync(toEmail, rendered.Subject, rendered.HtmlBody, ct);

        logger.LogInformation(
            "FeedbackMailService: test adult-individual email sent to {ToEmail} (game {GameId}, isReminder={IsReminder}).",
            toEmail, gameId, isReminder);
    }
}
