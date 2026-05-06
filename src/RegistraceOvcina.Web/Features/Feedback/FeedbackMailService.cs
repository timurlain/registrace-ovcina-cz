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
    /// Renders + sends a real bundle (household-contact) feedback email to
    /// <paramref name="toEmail"/> using the same renderer + sender + per-game
    /// template overrides as the real bulk send. Resolves the logged-in
    /// organizer's own <see cref="RegistrationSubmission"/> for this game and
    /// uses the actual Registrations + freshly issued FeedbackTokens, so the
    /// links in the email lead to real, working feedback forms. This makes the
    /// test a fully functional preview — the organizer can click through and
    /// experience the form end-to-end before triggering the bulk send.
    /// <para>Does NOT call <see cref="FeedbackService.MarkInvitedAsync"/>; a
    /// test send must not pollute the dashboard's "Pozváno" count.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="toEmail"/> is null/blank, when the game
    /// does not exist, when <see cref="FeedbackOptions.PublicBaseUrl"/> is
    /// unconfigured (mirrors the real-send safeguard), or when the logged-in
    /// user has no own submission in this game.
    /// </exception>
    public async Task SendTestBundleAsync(
        int gameId,
        string currentUserId,
        string toEmail,
        bool isReminder,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            throw new InvalidOperationException("Cílová e-mailová adresa je prázdná.");
        }
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            throw new InvalidOperationException("Přihlášený uživatel není rozpoznán.");
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

        // Resolve the logged-in user's own submission for this game. This is
        // the same predicate FeedbackScribe uses for ownership checks: the
        // submission's RegistrantUserId === current user, GameId matches, and
        // the soft-deleted flag is off. Tracked load (no AsNoTracking) so the
        // FeedbackTokenService writes below participate in change tracking
        // through its own DbContext — but we still re-include navigations
        // here to render names without a second round-trip.
        var submission = await db.RegistrationSubmissions
            .AsNoTracking()
            .Include(s => s.Registrations)
                .ThenInclude(r => r.Person)
            .FirstOrDefaultAsync(
                s => s.GameId == gameId
                    && s.RegistrantUserId == currentUserId
                    && !s.IsDeleted,
                ct)
            ?? throw new InvalidOperationException(
                "Test bundle e-mail vyžaduje vaši vlastní přihlášku do této hry.");

        // Lazily mint tokens for any registration that doesn't have one yet.
        // EnsureTokenAsync is idempotent — never rotates an existing token —
        // so it's safe to call from the test path. Real bulk-send will call
        // it again later for the same registrations and get the same Guids.
        var tokens = new Dictionary<int, Guid>(submission.Registrations.Count);
        foreach (var reg in submission.Registrations)
        {
            ct.ThrowIfCancellationRequested();
            tokens[reg.Id] = await tokenService.EnsureTokenAsync(reg.Id, ct);
        }

        // Mirror FeedbackService.ComputeWindow: closes-at falls back to
        // EndsAtUtc + 30 days when not configured. Keeps the rendered deadline
        // string consistent with whatever the organizer sees on the dashboard.
        var endsAt = new DateTimeOffset(DateTime.SpecifyKind(game.EndsAtUtc, DateTimeKind.Utc));
        var closesAt = game.FeedbackClosesAtUtc ?? endsAt.AddDays(30);

        var entries = submission.Registrations
            .OrderBy(r => r.Id)
            .Select(r => new FeedbackBundleEntry(
                AttendeeName: $"{r.Person.FirstName} {r.Person.LastName}".Trim(),
                AttendeeType: r.AttendeeType,
                TokenLink: $"{baseUrl}/zpetna-vazba/{tokens[r.Id]}"))
            .ToList();

        var sample = new FeedbackContactBundleEmail(
            ToEmail: toEmail,
            ContactName: string.IsNullOrWhiteSpace(submission.PrimaryContactName)
                ? toEmail
                : submission.PrimaryContactName,
            GameName: game.Name,
            FeedbackClosesAtLocal: closesAt,
            Entries: entries,
            IsReminder: isReminder,
            SubjectTemplate: game.FeedbackBundleSubjectTemplate,
            HtmlTemplate: game.FeedbackBundleHtmlTemplate);

        var rendered = emailRenderer.RenderContactBundle(sample);
        await emailSender.SendAsync(toEmail, rendered.Subject, rendered.HtmlBody, ct);

        logger.LogInformation(
            "FeedbackMailService: test bundle email sent to {ToEmail} (game {GameId}, submission {SubmissionId}, isReminder={IsReminder}).",
            toEmail, gameId, submission.Id, isReminder);
    }

    /// <summary>
    /// Renders + sends a real adult-individual feedback email to
    /// <paramref name="toEmail"/> using the same renderer + sender + per-game
    /// template overrides as the real bulk send. Resolves the logged-in
    /// user's own adult Registration in this game (matched by Person.Email
    /// case-insensitively against <paramref name="toEmail"/>) and uses its
    /// real FeedbackToken so the link in the email leads to a working form.
    /// <para>Does NOT call <see cref="FeedbackService.MarkInvitedAsync"/>; a
    /// test send must not pollute the dashboard's "Pozváno" count.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="toEmail"/> is null/blank, when the game
    /// does not exist, when <see cref="FeedbackOptions.PublicBaseUrl"/> is
    /// unconfigured, or when the logged-in user has no adult registration in
    /// this game (matched by email).
    /// </exception>
    public async Task SendTestAdultIndividualAsync(
        int gameId,
        string currentUserId,
        string toEmail,
        bool isReminder,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            throw new InvalidOperationException("Cílová e-mailová adresa je prázdná.");
        }
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            throw new InvalidOperationException("Přihlášený uživatel není rozpoznán.");
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

        // Mirror the real bulk-send classifier: an "adult-individual" recipient
        // is a Registration where AttendeeType != Player AND Person.Email is
        // set. The simplest robust mapping from the logged-in user to such a
        // Registration is by email: Person.Email == currentUser.Email
        // case-insensitive, joined to a non-deleted submission in this game.
        // EF Core in-memory provider doesn't honour StringComparison overloads
        // on Where, so we lower both sides explicitly.
        var emailLower = toEmail.Trim().ToLowerInvariant();
        var registration = await db.Registrations
            .AsNoTracking()
            .Include(r => r.Person)
            .Include(r => r.Submission)
            .Where(r => r.Submission.GameId == gameId
                && !r.Submission.IsDeleted
                && r.AttendeeType != AttendeeType.Player
                && r.Person.Email != null
                && r.Person.Email.ToLower() == emailLower)
            .OrderBy(r => r.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException(
                "Test e-mail pro dospělé vyžaduje, abyste byli sami zaregistrováni jako dospělý účastník této hry.");

        // Lazy token issue (idempotent — never rotates).
        var token = await tokenService.EnsureTokenAsync(registration.Id, ct);

        var endsAt = new DateTimeOffset(DateTime.SpecifyKind(game.EndsAtUtc, DateTimeKind.Utc));
        var closesAt = game.FeedbackClosesAtUtc ?? endsAt.AddDays(30);

        var sample = new FeedbackAdultIndividualEmail(
            ToEmail: toEmail,
            AttendeeName: $"{registration.Person.FirstName} {registration.Person.LastName}".Trim(),
            GameName: game.Name,
            FeedbackClosesAtLocal: closesAt,
            TokenLink: $"{baseUrl}/zpetna-vazba/{token}",
            IsReminder: isReminder,
            SubjectTemplate: game.FeedbackAdultIndividualSubjectTemplate,
            HtmlTemplate: game.FeedbackAdultIndividualHtmlTemplate);

        var rendered = emailRenderer.RenderAdultIndividual(sample);
        await emailSender.SendAsync(toEmail, rendered.Subject, rendered.HtmlBody, ct);

        logger.LogInformation(
            "FeedbackMailService: test adult-individual email sent to {ToEmail} (game {GameId}, registration {RegistrationId}, isReminder={IsReminder}).",
            toEmail, gameId, registration.Id, isReminder);
    }
}
