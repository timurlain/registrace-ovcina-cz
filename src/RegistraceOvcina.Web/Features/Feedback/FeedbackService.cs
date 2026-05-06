using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.CharacterPrep; // PagedResult<T> reused

namespace RegistraceOvcina.Web.Features.Feedback;

/// <summary>
/// Heart of the post-game feedback feature: loads a feedback view for a single
/// Registration, saves answer drafts with audit trail, marks responses as
/// Submitted, and derives a three-state status for dashboards.
/// </summary>
/// <remarks>
/// <para>The save / submit boundary is explicit: <see cref="SaveAsync"/> only
/// writes draft answers (does NOT set <c>SubmittedAtUtc</c>);
/// <see cref="MarkSubmittedAsync"/> finalises the response. Both operations
/// stamp <c>UpdatedAtUtc</c>.</para>
/// <para>The open / close window for a game is computed from
/// <see cref="Game.FeedbackOpensAtUtc"/> and <see cref="Game.FeedbackClosesAtUtc"/>,
/// falling back to <c>EndsAtUtc</c> and <c>EndsAtUtc + 30 days</c> respectively
/// when either column is null. Outside that window, <see cref="GetViewAsync"/>
/// returns a read-only view and <see cref="SaveAsync"/> rejects with a stable
/// reason code.</para>
/// </remarks>
public sealed class FeedbackService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    FeedbackOptionsService optionsService,
    TimeProvider timeProvider)
{
    // Czech diacritics are routine in answers ("řeč", "mě bavil obchod…").
    // The default JSON encoder escapes them to \uXXXX which round-trips fine
    // technically, but is hostile to anyone who pulls AnswersJson out of the
    // database to read it. UnsafeRelaxedJsonEscaping keeps the bytes legible.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<FeedbackView?> GetViewAsync(int registrationId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var registration = await db.Registrations
            .AsNoTracking()
            .Include(x => x.Person)
            .Include(x => x.Submission)
            .Include(x => x.FeedbackResponse)
            .FirstOrDefaultAsync(x => x.Id == registrationId, cancellationToken);

        if (registration is null)
        {
            // Either no such row, or the soft-delete query filter on Submission
            // hid the registration. Treat both as "not found" — callers must
            // respond with 404, not leak existence.
            return null;
        }

        var game = await db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == registration.Submission.GameId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Game {registration.Submission.GameId} for registration {registrationId} not found.");

        var (opensAt, closesAt) = ComputeWindow(game);
        var now = timeProvider.GetUtcNow();
        var (isReadOnly, reason) = ResolveReadOnly(now, opensAt, closesAt);

        var questions = await optionsService.GetForRoleAsync(
            game.Id, registration.AttendeeType, cancellationToken);

        var answers = DeserializeAnswers(registration.FeedbackResponse?.AnswersJson);

        return new FeedbackView(
            RegistrationId: registration.Id,
            SubmissionId: registration.SubmissionId,
            GameId: game.Id,
            GameName: game.Name,
            PersonId: registration.PersonId,
            AttendeeFullName: $"{registration.Person.FirstName} {registration.Person.LastName}",
            AttendeeType: registration.AttendeeType,
            Questions: questions,
            Answers: answers,
            UpdatedAtUtc: registration.FeedbackResponse?.UpdatedAtUtc,
            SubmittedAtUtc: registration.FeedbackResponse?.SubmittedAtUtc,
            EffectiveOpensAt: opensAt,
            EffectiveClosesAt: closesAt,
            IsReadOnly: isReadOnly,
            ReadOnlyReason: reason);
    }

    public async Task<FeedbackSaveResult> SaveAsync(
        int registrationId,
        IReadOnlyDictionary<string, string> answers,
        FeedbackEditedBy editedBy,
        int? editedByPersonId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(answers);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var registration = await db.Registrations
            .Include(x => x.Submission)
            .Include(x => x.FeedbackResponse)
            .FirstOrDefaultAsync(x => x.Id == registrationId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Registration {registrationId} not found.");

        var game = await db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == registration.Submission.GameId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Game {registration.Submission.GameId} for registration {registrationId} not found.");

        var (opensAt, closesAt) = ComputeWindow(game);
        var now = timeProvider.GetUtcNow();

        if (now < opensAt)
        {
            return new FeedbackSaveResult(false, "NotOpen");
        }

        if (now > closesAt)
        {
            return new FeedbackSaveResult(false, "Closed");
        }

        var questionSet = await optionsService.GetForRoleAsync(
            game.Id, registration.AttendeeType, cancellationToken);

        var validKeys = new HashSet<string>(
            questionSet.Questions.Select(q => q.Key),
            StringComparer.Ordinal);

        // Merge incoming answers into the existing blob (do NOT whole-blob
        // replace). For each incoming entry we:
        //   - skip unknown keys (defends against schema renames where an old
        //     form posts a stale key);
        //   - skip blanks — a blank in the new payload must NOT overwrite an
        //     existing non-blank value (kid pressed Hotovo, came back next
        //     morning, only filled one field; we keep yesterday's other
        //     answers intact).
        // Keys not mentioned in the incoming dict are also preserved as-is.
        var merged = new Dictionary<string, string>(
            DeserializeAnswers(registration.FeedbackResponse?.AnswersJson),
            StringComparer.Ordinal);

        foreach (var (key, value) in answers)
        {
            if (!validKeys.Contains(key))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            merged[key] = value;
        }

        var json = JsonSerializer.Serialize(merged, JsonOptions);

        if (registration.FeedbackResponse is null)
        {
            var created = new FeedbackResponse
            {
                RegistrationId = registration.Id,
                AnswersJson = json,
                LastEditedBy = editedBy,
                LastEditedByPersonId = editedByPersonId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            db.FeedbackResponses.Add(created);
        }
        else
        {
            registration.FeedbackResponse.AnswersJson = json;
            registration.FeedbackResponse.LastEditedBy = editedBy;
            registration.FeedbackResponse.LastEditedByPersonId = editedByPersonId;
            registration.FeedbackResponse.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new FeedbackSaveResult(true, null);
    }

    public async Task MarkSubmittedAsync(int registrationId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var response = await db.FeedbackResponses
            .FirstOrDefaultAsync(x => x.RegistrationId == registrationId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"FeedbackResponse for registration {registrationId} not found. " +
                "Save a draft before marking submitted.");

        var now = timeProvider.GetUtcNow();

        // Idempotent: a re-submission keeps the original SubmittedAtUtc so the
        // audit trail still says "first submitted at X". UpdatedAtUtc still
        // ticks so dashboards show recent activity.
        response.SubmittedAtUtc ??= now;
        response.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<FeedbackStatus?> GetStatusAsync(int registrationId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var row = await db.Registrations
            .AsNoTracking()
            .Where(x => x.Id == registrationId)
            .Select(x => new
            {
                x.FeedbackInvitedAtUtc,
                ResponseSubmittedAtUtc = x.FeedbackResponse != null ? x.FeedbackResponse.SubmittedAtUtc : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        if (row.ResponseSubmittedAtUtc is not null)
        {
            return FeedbackStatus.Done;
        }

        if (row.FeedbackInvitedAtUtc is null)
        {
            return FeedbackStatus.NotInvited;
        }

        return FeedbackStatus.Waiting;
    }

    /// <summary>
    /// Computes the effective open and close window for a game. When either
    /// column on <see cref="Game"/> is null we fall back to:
    /// <list type="bullet">
    ///   <item>opens at: <c>EndsAtUtc</c> (game over → feedback opens)</item>
    ///   <item>closes at: <c>EndsAtUtc + 30 days</c></item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <see cref="Game.EndsAtUtc"/> is a <see cref="DateTime"/> (not a
    /// <see cref="DateTimeOffset"/>) and is stored in UTC by convention. We
    /// pin it with <see cref="DateTimeKind.Utc"/> before constructing the
    /// offset to avoid local-time interpretation by the in-memory provider.
    /// This mirrors the pattern used by <c>CharacterPrepService</c>.
    /// </remarks>
    private static (DateTimeOffset opensAt, DateTimeOffset closesAt) ComputeWindow(Game game)
    {
        var endsAt = new DateTimeOffset(DateTime.SpecifyKind(game.EndsAtUtc, DateTimeKind.Utc));
        var opens = game.FeedbackOpensAtUtc ?? endsAt;
        var closes = game.FeedbackClosesAtUtc ?? endsAt.AddDays(30);
        return (opens, closes);
    }

    private static (bool IsReadOnly, string? Reason) ResolveReadOnly(
        DateTimeOffset now,
        DateTimeOffset opensAt,
        DateTimeOffset closesAt)
    {
        // Read-only is determined ONLY by the open/close window. Submission
        // (SubmittedAtUtc) is a status signal for the dashboard ("Done" vs
        // "Waiting"), NOT a lock — kids legitimately remember more the next
        // morning, and a parent scribing for them needs to keep editing after
        // pressing "Hotovo". The dashboard still treats SubmittedAtUtc as
        // "Done" via GetStatusAsync; that doesn't pin the form.
        if (now < opensAt)
        {
            return (true, "NotOpen");
        }

        if (now > closesAt)
        {
            return (true, "Closed");
        }

        return (false, null);
    }

    // -------------------------------------------------------------------------
    // Organizer dashboard
    // -------------------------------------------------------------------------

    /// <summary>
    /// Aggregate counts for the dashboard tile row, scoped to one game.
    /// Soft-deleted submissions are excluded by the global query filter on
    /// <see cref="RegistrationSubmission"/>.
    /// </summary>
    public async Task<FeedbackStats> GetDashboardStatsAsync(int gameId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // One server round-trip; aggregates are computed in-memory on a tiny
        // boolean projection (a few hundred rows max for a LARP).
        var aggregates = await db.Registrations
            .AsNoTracking()
            .Where(r => r.Submission.GameId == gameId)
            .Select(r => new
            {
                Invited = r.FeedbackInvitedAtUtc != null,
                Submitted = r.FeedbackResponse != null && r.FeedbackResponse.SubmittedAtUtc != null,
            })
            .ToListAsync(cancellationToken);

        var total = aggregates.Count;
        var invited = aggregates.Count(a => a.Invited);
        var done = aggregates.Count(a => a.Submitted);
        // Waiting = invited but not yet submitted. (Not-invited rows are not
        // "waiting" — organizers haven't asked them yet.)
        var waiting = invited - done;

        return new FeedbackStats(total, invited, done, waiting);
    }

    /// <summary>
    /// Paginated, filterable, one-row-per-Registration projection for the
    /// organizer dashboard. Soft-deleted submissions are excluded by the
    /// global query filter on <see cref="RegistrationSubmission"/>.
    /// </summary>
    /// <remarks>
    /// Sort: <see cref="FeedbackStatus"/> ascending (NotInvited first), then
    /// HouseholdName, then PersonFullName — mirrors the CharacterPrep
    /// dashboard so organizers see actionable rows at the top. Status is a
    /// derived three-way enum so we project to memory and sort there; the
    /// dataset is small (≤ a few hundred attendees per game).
    /// </remarks>
    public async Task<PagedResult<FeedbackDashboardRow>> GetDashboardRowsAsync(
        int gameId,
        FeedbackDashboardFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var raw = await db.Registrations
            .AsNoTracking()
            .Where(r => r.Submission.GameId == gameId)
            .Select(r => new
            {
                r.Id,
                r.SubmissionId,
                HouseholdName = r.Submission.PrimaryContactName,
                PersonFirst = r.Person.FirstName,
                PersonLast = r.Person.LastName,
                r.AttendeeType,
                InvitedAtUtc = r.FeedbackInvitedAtUtc,
                ResponseSubmittedAtUtc = r.FeedbackResponse != null
                    ? r.FeedbackResponse.SubmittedAtUtc
                    : null,
                LastEditedBy = r.FeedbackResponse != null
                    ? r.FeedbackResponse.LastEditedBy
                    : (FeedbackEditedBy?)null,
                ResponseUpdatedAtUtc = r.FeedbackResponse != null
                    ? r.FeedbackResponse.UpdatedAtUtc
                    : (DateTimeOffset?)null,
                r.FeedbackToken,
            })
            .ToListAsync(cancellationToken);

        var rows = raw
            .Select(r => new FeedbackDashboardRow(
                RegistrationId: r.Id,
                SubmissionId: r.SubmissionId,
                HouseholdName: r.HouseholdName,
                PersonFullName: (r.PersonFirst + " " + r.PersonLast).Trim(),
                AttendeeType: r.AttendeeType,
                Status: DeriveDashboardStatus(r.InvitedAtUtc, r.ResponseSubmittedAtUtc),
                LastEditedBy: r.LastEditedBy,
                UpdatedAtUtc: r.ResponseUpdatedAtUtc,
                SubmittedAtUtc: r.ResponseSubmittedAtUtc,
                FeedbackToken: r.FeedbackToken))
            .ToList();

        if (filter.Status is FeedbackStatus wantedStatus)
        {
            rows = rows.Where(r => r.Status == wantedStatus).ToList();
        }

        if (filter.AttendeeType is AttendeeType wantedType)
        {
            rows = rows.Where(r => r.AttendeeType == wantedType).ToList();
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var needle = filter.Search.Trim();
            rows = rows
                .Where(r =>
                    (!string.IsNullOrEmpty(r.PersonFullName)
                        && r.PersonFullName.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(r.HouseholdName)
                        && r.HouseholdName.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        var total = rows.Count;

        var ordered = rows
            .OrderBy(r => (int)r.Status)
            .ThenBy(r => r.HouseholdName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.PersonFullName, StringComparer.CurrentCultureIgnoreCase)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PagedResult<FeedbackDashboardRow>(ordered, total, page, pageSize);
    }

    /// <summary>
    /// Registrations targeted by the bulk "Poslat pozvánky" button: those
    /// that have not yet been invited. Soft-deleted submissions are excluded
    /// by the global query filter.
    /// </summary>
    public async Task<IReadOnlyList<Registration>> ListInvitationTargetsAsync(
        int gameId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // AsNoTracking — the caller hands ids off to MarkInvitedAsync which
        // re-fetches with tracking, so we don't need change tracking here.
        return await db.Registrations
            .AsNoTracking()
            .Where(r => r.Submission.GameId == gameId
                && r.FeedbackInvitedAtUtc == null)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Registrations targeted by the bulk "Poslat připomínku" button:
    /// already invited, not yet submitted, and either never reminded or last
    /// reminded more than 24 hours ago. The 24h threshold prevents
    /// double-tapping the bulk button from sending two reminders in quick
    /// succession.
    /// </summary>
    public async Task<IReadOnlyList<Registration>> ListReminderTargetsAsync(
        int gameId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var threshold = nowUtc.AddHours(-24);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Registrations
            .AsNoTracking()
            .Where(r => r.Submission.GameId == gameId
                && r.FeedbackInvitedAtUtc != null
                && (r.FeedbackResponse == null || r.FeedbackResponse.SubmittedAtUtc == null)
                && (r.FeedbackReminderLastSentAtUtc == null
                    || r.FeedbackReminderLastSentAtUtc < threshold))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Stamps <see cref="Registration.FeedbackInvitedAtUtc"/> with
    /// <paramref name="nowUtc"/>. Idempotent: once invited, re-invocation is
    /// a no-op so the original invitation timestamp is preserved (the audit
    /// trail says "first invited at X").
    /// </summary>
    public async Task MarkInvitedAsync(
        int registrationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var registration = await db.Registrations
            .FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Registration {registrationId} not found.");

        if (registration.FeedbackInvitedAtUtc is null)
        {
            registration.FeedbackInvitedAtUtc = nowUtc;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Stamps <see cref="Registration.FeedbackReminderLastSentAtUtc"/> with
    /// <paramref name="nowUtc"/>. Always overwrites — the bulk button uses
    /// <see cref="ListReminderTargetsAsync"/> to enforce the 24h window, but
    /// direct callers may legitimately re-send sooner (e.g. organiser
    /// resending after a bounce).
    /// </summary>
    public async Task MarkReminderSentAsync(
        int registrationId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var registration = await db.Registrations
            .FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Registration {registrationId} not found.");

        registration.FeedbackReminderLastSentAtUtc = nowUtc;
        await db.SaveChangesAsync(cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Organizer window override (open / close / extend)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sets <see cref="Game.FeedbackOpensAtUtc"/> to <paramref name="nowUtc"/>.
    /// If <see cref="Game.FeedbackClosesAtUtc"/> is null or in the past, also
    /// bumps it to <paramref name="nowUtc"/> + 30 days; otherwise leaves the
    /// close timestamp untouched. Throws when the game does not exist.
    /// </summary>
    public async Task OpenFeedbackAsync(
        int gameId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var game = await db.Games
            .FirstOrDefaultAsync(x => x.Id == gameId, cancellationToken)
            ?? throw new InvalidOperationException($"Game {gameId} not found.");

        game.FeedbackOpensAtUtc = nowUtc;

        if (game.FeedbackClosesAtUtc is null || game.FeedbackClosesAtUtc <= nowUtc)
        {
            game.FeedbackClosesAtUtc = nowUtc.AddDays(30);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Sets <see cref="Game.FeedbackClosesAtUtc"/> to <paramref name="nowUtc"/>,
    /// effectively shutting the form. Throws when the game does not exist.
    /// </summary>
    public async Task CloseFeedbackAsync(
        int gameId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var game = await db.Games
            .FirstOrDefaultAsync(x => x.Id == gameId, cancellationToken)
            ?? throw new InvalidOperationException($"Game {gameId} not found.");

        game.FeedbackClosesAtUtc = nowUtc;

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Sets <see cref="Game.FeedbackClosesAtUtc"/> to
    /// <c>max(nowUtc, current) + 30 days</c>. Throws when the game does not
    /// exist.
    /// </summary>
    public async Task ExtendFeedbackAsync(
        int gameId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var game = await db.Games
            .FirstOrDefaultAsync(x => x.Id == gameId, cancellationToken)
            ?? throw new InvalidOperationException($"Game {gameId} not found.");

        var anchor = game.FeedbackClosesAtUtc is { } existing && existing > nowUtc
            ? existing
            : nowUtc;

        game.FeedbackClosesAtUtc = anchor.AddDays(30);

        await db.SaveChangesAsync(cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Organizer per-response actions (pin to chronicle, organizer note)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Toggles <see cref="FeedbackResponse.PinToChronicle"/> on the response
    /// belonging to <paramref name="registrationId"/>. Throws when no response
    /// row exists yet (pinning an empty response is meaningless).
    /// </summary>
    public async Task SetPinToChronicleAsync(
        int registrationId,
        bool pin,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var response = await db.FeedbackResponses
            .FirstOrDefaultAsync(x => x.RegistrationId == registrationId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"FeedbackResponse for registration {registrationId} not found. " +
                "A response must exist before it can be pinned.");

        response.PinToChronicle = pin;
        response.UpdatedAtUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Persists <paramref name="note"/> to
    /// <see cref="FeedbackResponse.OrganizerNote"/>. Blank input is normalized
    /// to <c>null</c> (the column nulls out — no empty strings stored). Throws
    /// when no response row exists yet.
    /// </summary>
    public async Task SaveOrganizerNoteAsync(
        int registrationId,
        string? note,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var response = await db.FeedbackResponses
            .FirstOrDefaultAsync(x => x.RegistrationId == registrationId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"FeedbackResponse for registration {registrationId} not found. " +
                "A response must exist before an organizer note can be saved.");

        response.OrganizerNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        response.UpdatedAtUtc = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Loads a single feedback response with the answers, schema, organizer
    /// note, and pin flag for the per-response detail page.
    /// </summary>
    public async Task<FeedbackResponseDetail?> GetResponseDetailAsync(
        int registrationId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var registration = await db.Registrations
            .AsNoTracking()
            .Include(x => x.Person)
            .Include(x => x.Submission)
            .Include(x => x.FeedbackResponse)
            .FirstOrDefaultAsync(x => x.Id == registrationId, cancellationToken);

        if (registration is null)
        {
            return null;
        }

        var game = await db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == registration.Submission.GameId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Game {registration.Submission.GameId} for registration {registrationId} not found.");

        var questions = await optionsService.GetForRoleAsync(
            game.Id, registration.AttendeeType, cancellationToken);

        var answers = DeserializeAnswers(registration.FeedbackResponse?.AnswersJson);
        var status = DeriveDashboardStatus(
            registration.FeedbackInvitedAtUtc,
            registration.FeedbackResponse?.SubmittedAtUtc);

        return new FeedbackResponseDetail(
            RegistrationId: registration.Id,
            GameId: game.Id,
            GameName: game.Name,
            SubmissionId: registration.SubmissionId,
            HouseholdName: registration.Submission.PrimaryContactName,
            PersonFullName: ($"{registration.Person.FirstName} {registration.Person.LastName}").Trim(),
            AttendeeType: registration.AttendeeType,
            Status: status,
            Questions: questions,
            Answers: answers,
            LastEditedBy: registration.FeedbackResponse?.LastEditedBy,
            UpdatedAtUtc: registration.FeedbackResponse?.UpdatedAtUtc,
            SubmittedAtUtc: registration.FeedbackResponse?.SubmittedAtUtc,
            HasResponse: registration.FeedbackResponse is not null,
            PinToChronicle: registration.FeedbackResponse?.PinToChronicle ?? false,
            OrganizerNote: registration.FeedbackResponse?.OrganizerNote,
            FeedbackToken: registration.FeedbackToken);
    }

    /// <summary>
    /// Loads minimal game header info for the organizer feedback dashboard:
    /// name + the effective open / close window. Returns <c>null</c> when the
    /// game does not exist.
    /// </summary>
    public async Task<FeedbackGameHeader?> GetGameHeaderAsync(
        int gameId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var game = await db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == gameId, cancellationToken);

        if (game is null)
        {
            return null;
        }

        var (opensAt, closesAt) = ComputeWindow(game);
        return new FeedbackGameHeader(game.Id, game.Name, opensAt, closesAt);
    }

    // -------------------------------------------------------------------------
    // Household-facing submission detail card
    // -------------------------------------------------------------------------

    /// <summary>
    /// One-shot listing of all attendees in a submission with their per-attendee
    /// feedback status. Powers the "Zpětná vazba k Ovčině {GameName}" card on
    /// the household submission detail page.
    /// </summary>
    /// <returns>
    /// <para>The list of attendees on the submission, ordered AttendeeType then
    /// last name then first name, when the feedback window is currently active.</para>
    /// <para><c>null</c> when:
    /// <list type="bullet">
    ///   <item>the submission doesn't exist (or is hidden by the soft-delete query filter), or</item>
    ///   <item>the game's feedback window has not yet opened, or</item>
    ///   <item>the game's feedback window has already closed.</item>
    /// </list>
    /// Callers use the null result as a "hide the card" signal.</para>
    /// </returns>
    public async Task<IReadOnlyList<SubmissionFeedbackAttendee>?> GetSubmissionAttendeesAsync(
        int submissionId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var submission = await db.RegistrationSubmissions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == submissionId, cancellationToken);

        if (submission is null)
        {
            // Either no such submission, or hidden by the IsDeleted query
            // filter on RegistrationSubmission. Treat both as "hide the card".
            return null;
        }

        var game = await db.Games
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == submission.GameId, cancellationToken);

        if (game is null)
        {
            return null;
        }

        var (opensAt, closesAt) = ComputeWindow(game);
        if (nowUtc < opensAt || nowUtc > closesAt)
        {
            // Window is not currently active — the household submission detail
            // page hides the card entirely outside the open window.
            return null;
        }

        var raw = await db.Registrations
            .AsNoTracking()
            .Where(r => r.SubmissionId == submissionId)
            .Select(r => new
            {
                r.Id,
                PersonFirst = r.Person.FirstName,
                PersonLast = r.Person.LastName,
                r.AttendeeType,
                InvitedAtUtc = r.FeedbackInvitedAtUtc,
                ResponseSubmittedAtUtc = r.FeedbackResponse != null
                    ? r.FeedbackResponse.SubmittedAtUtc
                    : null,
            })
            .ToListAsync(cancellationToken);

        // Order: players first (children are the focus), then adults; then by
        // last+first name. Cultural ordering keeps Czech diacritics stable.
        return raw
            .OrderBy(r => (int)r.AttendeeType)
            .ThenBy(r => r.PersonLast, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.PersonFirst, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => new SubmissionFeedbackAttendee(
                RegistrationId: r.Id,
                PersonFullName: (r.PersonFirst + " " + r.PersonLast).Trim(),
                AttendeeType: r.AttendeeType,
                Status: DeriveDashboardStatus(r.InvitedAtUtc, r.ResponseSubmittedAtUtc)))
            .ToList();
    }

    private static FeedbackStatus DeriveDashboardStatus(
        DateTimeOffset? invitedAtUtc,
        DateTimeOffset? responseSubmittedAtUtc)
    {
        if (responseSubmittedAtUtc is not null)
        {
            return FeedbackStatus.Done;
        }

        if (invitedAtUtc is null)
        {
            return FeedbackStatus.NotInvited;
        }

        return FeedbackStatus.Waiting;
    }

    private static IReadOnlyDictionary<string, string> DeserializeAnswers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            return dict ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            // A malformed AnswersJson is a bug, not a user-facing error — but
            // we don't want a corrupt single row to take down the form for
            // everyone. Fall back to empty so the user can re-fill.
            return new Dictionary<string, string>();
        }
    }
}
