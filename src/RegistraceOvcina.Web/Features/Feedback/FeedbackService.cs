using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

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
