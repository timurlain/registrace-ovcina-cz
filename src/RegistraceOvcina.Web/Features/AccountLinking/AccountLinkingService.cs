using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.AccountLinking;

public enum LinkSignal
{
    ExactEmailMatch,
    AlternateEmailMatch,
    SubmissionPrimaryContactMatch,
    FuzzyNameMatch
}

public sealed record LinkProposal(
    string UserId,
    string UserEmail,
    string? UserDisplayName,
    int PersonId,
    string PersonFullName,
    LinkSignal Signal,
    int? FuzzyScore = null);

public sealed record ProposalBucket(
    IReadOnlyList<LinkProposal> HighConfidence,
    IReadOnlyList<LinkProposal> MediumConfidence);

public sealed record AlreadyLinkedView(
    string UserId,
    string UserEmail,
    string? UserDisplayName,
    int PersonId,
    string PersonFullName,
    DateTimeOffset? LinkedAtUtc);

public sealed record UnlinkedPersonView(
    int PersonId,
    string PersonFullName,
    int? BirthYear,
    string? Email);

public sealed record UserPickerResult(
    string UserId,
    string Email,
    string? DisplayName);

public interface IAccountLinkingService
{
    Task<ProposalBucket> ProposeAsync(CancellationToken ct);
    Task<int> AutoLinkHighConfidenceAsync(string actorUserId, CancellationToken ct);
    Task LinkAsync(string userId, int personId, string actorUserId, CancellationToken ct);
    Task UnlinkAsync(string userId, string actorUserId, CancellationToken ct);
    Task<IReadOnlyList<AlreadyLinkedView>> ListLinkedAsync(CancellationToken ct);
    Task<IReadOnlyList<UnlinkedPersonView>> ListUnlinkedPersonsAsync(CancellationToken ct);
    Task<IReadOnlyList<UserPickerResult>> SearchUsersAsync(string query, int limit, CancellationToken ct);
}

public sealed class AccountLinkingService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    TimeProvider timeProvider,
    ILogger<AccountLinkingService> logger)
    : IAccountLinkingService
{
    private const int FuzzyScoreThreshold = 75;
    private const int MaxLevenshteinDistance = 2;

    public async Task<ProposalBucket> ProposeAsync(CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // Only propose for ApplicationUsers that aren't already linked.
        var unlinkedUsers = await db.Users.AsNoTracking()
            .Where(u => u.PersonId == null)
            .Select(u => new UnlinkedUser(
                u.Id,
                u.Email ?? "",
                u.NormalizedEmail ?? "",
                u.DisplayName))
            .ToListAsync(ct);

        // Only propose Persons that have no ApplicationUser pointing at them.
        // NOTE: this also protects against proposing a Person that's already claimed —
        // ProposeAsync filters them out upfront so the admin page never offers them.
        var linkedPersonIds = await db.Users.AsNoTracking()
            .Where(u => u.PersonId != null)
            .Select(u => u.PersonId!.Value)
            .ToListAsync(ct);
        var linkedPersonSet = new HashSet<int>(linkedPersonIds);

        var unlinkedPersons = await db.People.AsNoTracking()
            .Select(p => new UnlinkedPerson(
                p.Id,
                p.FirstName,
                p.LastName,
                p.Email))
            .ToListAsync(ct);

        unlinkedPersons = unlinkedPersons
            .Where(p => !linkedPersonSet.Contains(p.PersonId))
            .ToList();

        // Group Persons by normalized email for exact email match.
        var personsByEmail = unlinkedPersons
            .Where(p => !string.IsNullOrWhiteSpace(p.Email))
            .GroupBy(p => p.Email!.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.ToList());

        // Alt emails lookup — userId → set of normalized emails.
        var altEmails = await db.UserEmails.AsNoTracking()
            .Select(ue => new { ue.UserId, ue.NormalizedEmail })
            .ToListAsync(ct);
        var altEmailsByUser = altEmails
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.NormalizedEmail).ToList());

        // Submissions: join to attendees that are Adults. We need each submission's PrimaryEmail,
        // PrimaryContactName, and the list of adult attendees (FirstName/LastName) through Registrations→Person.
        var submissionRows = await db.RegistrationSubmissions.AsNoTracking()
            .Where(s => !s.IsDeleted)
            .Select(s => new SubmissionRow(
                s.Id,
                s.PrimaryEmail,
                s.PrimaryContactName,
                s.Registrations
                    .Where(r => r.AttendeeType == AttendeeType.Adult
                                && r.Status == RegistrationStatus.Active
                                && !r.Person.IsDeleted)
                    .Select(r => new AdultAttendee(r.Person.Id, r.Person.FirstName, r.Person.LastName))
                    .ToList()))
            .ToListAsync(ct);

        // Group submissions by normalized primary email for fast lookup.
        var submissionsByEmail = submissionRows
            .Where(s => !string.IsNullOrWhiteSpace(s.PrimaryEmail))
            .GroupBy(s => s.PrimaryEmail.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.ToList());

        var highConfidence = new List<LinkProposal>();
        var mediumConfidence = new List<LinkProposal>();
        var usersMatchedHigh = new HashSet<string>(StringComparer.Ordinal);

        foreach (var user in unlinkedUsers)
        {
            if (string.IsNullOrWhiteSpace(user.NormalizedEmail)) continue;

            // 1. Exact email match (Person.Email == User.NormalizedEmail).
            if (personsByEmail.TryGetValue(user.NormalizedEmail, out var matchedByEmail))
            {
                if (matchedByEmail.Count == 1)
                {
                    var p = matchedByEmail[0];
                    highConfidence.Add(new LinkProposal(
                        user.UserId, user.Email, user.DisplayName,
                        p.PersonId, FullName(p.FirstName, p.LastName),
                        LinkSignal.ExactEmailMatch));
                    usersMatchedHigh.Add(user.UserId);
                    continue;
                }
                // Ambiguous — multiple Persons share the email. Skip (tie).
                usersMatchedHigh.Add(user.UserId);
                continue;
            }

            // 2. Alternate email match — any of this user's alt emails lands on a Person.
            if (altEmailsByUser.TryGetValue(user.UserId, out var alts))
            {
                var matchedPersons = new List<UnlinkedPerson>();
                foreach (var alt in alts)
                {
                    if (personsByEmail.TryGetValue(alt, out var list))
                    {
                        matchedPersons.AddRange(list);
                    }
                }
                // Dedup by PersonId.
                var distinct = matchedPersons.DistinctBy(p => p.PersonId).ToList();
                if (distinct.Count == 1)
                {
                    var p = distinct[0];
                    highConfidence.Add(new LinkProposal(
                        user.UserId, user.Email, user.DisplayName,
                        p.PersonId, FullName(p.FirstName, p.LastName),
                        LinkSignal.AlternateEmailMatch));
                    usersMatchedHigh.Add(user.UserId);
                    continue;
                }
                if (distinct.Count > 1)
                {
                    // Ambiguous — skip.
                    usersMatchedHigh.Add(user.UserId);
                    continue;
                }
            }

            // 3. SubmissionPrimaryContactMatch — user.NormalizedEmail == submission.PrimaryEmail (normalized)
            //    AND there's exactly one adult attendee whose FirstName+LastName matches PrimaryContactName.
            if (submissionsByEmail.TryGetValue(user.NormalizedEmail, out var subList))
            {
                var candidates = new List<UnlinkedPerson>();
                foreach (var sub in subList)
                {
                    var (contactFirst, contactLast) = SplitFullName(sub.PrimaryContactName);
                    if (string.IsNullOrWhiteSpace(contactFirst) && string.IsNullOrWhiteSpace(contactLast))
                        continue;

                    var normContactFirst = NormalizeForNameCompare(contactFirst);
                    var normContactLast = NormalizeForNameCompare(contactLast);

                    foreach (var adult in sub.AdultAttendees)
                    {
                        // Skip if this attendee is already linked.
                        if (linkedPersonSet.Contains(adult.PersonId)) continue;

                        if (NormalizeForNameCompare(adult.FirstName) == normContactFirst
                            && NormalizeForNameCompare(adult.LastName) == normContactLast)
                        {
                            candidates.Add(new UnlinkedPerson(
                                adult.PersonId, adult.FirstName, adult.LastName, null));
                        }
                    }
                }

                var distinctCandidates = candidates.DistinctBy(p => p.PersonId).ToList();
                if (distinctCandidates.Count == 1)
                {
                    var p = distinctCandidates[0];
                    highConfidence.Add(new LinkProposal(
                        user.UserId, user.Email, user.DisplayName,
                        p.PersonId, FullName(p.FirstName, p.LastName),
                        LinkSignal.SubmissionPrimaryContactMatch));
                    usersMatchedHigh.Add(user.UserId);
                    continue;
                }
                if (distinctCandidates.Count > 1)
                {
                    // Tie — skip.
                    usersMatchedHigh.Add(user.UserId);
                    continue;
                }
            }
        }

        // 4. FuzzyNameMatch — only for users without any high-confidence match.
        foreach (var user in unlinkedUsers)
        {
            if (usersMatchedHigh.Contains(user.UserId)) continue;

            var (userFirst, userLast) = SplitFullName(user.DisplayName);
            if (string.IsNullOrWhiteSpace(userLast)) continue;

            var normUserFirst = NormalizeForNameCompare(userFirst);
            var normUserLast = NormalizeForNameCompare(userLast);

            var bestMatches = new List<(UnlinkedPerson Person, int Score)>();

            foreach (var person in unlinkedPersons)
            {
                var normPersonFirst = NormalizeForNameCompare(person.FirstName);
                var normPersonLast = NormalizeForNameCompare(person.LastName);

                if (normPersonLast != normUserLast) continue;

                var distance = LevenshteinDistance(normUserFirst, normPersonFirst);
                if (distance > MaxLevenshteinDistance) continue;

                var score = 100 - distance * 10;
                if (score >= FuzzyScoreThreshold)
                {
                    bestMatches.Add((person, score));
                }
            }

            if (bestMatches.Count == 1)
            {
                var (p, score) = bestMatches[0];
                mediumConfidence.Add(new LinkProposal(
                    user.UserId, user.Email, user.DisplayName,
                    p.PersonId, FullName(p.FirstName, p.LastName),
                    LinkSignal.FuzzyNameMatch, score));
            }
            else if (bestMatches.Count > 1)
            {
                // For medium confidence, still surface them — take top score, but if tied skip.
                var ordered = bestMatches.OrderByDescending(x => x.Score).ToList();
                if (ordered[0].Score > ordered[1].Score)
                {
                    var (p, score) = ordered[0];
                    mediumConfidence.Add(new LinkProposal(
                        user.UserId, user.Email, user.DisplayName,
                        p.PersonId, FullName(p.FirstName, p.LastName),
                        LinkSignal.FuzzyNameMatch, score));
                }
            }
        }

        return new ProposalBucket(highConfidence, mediumConfidence);
    }

    public async Task<int> AutoLinkHighConfidenceAsync(string actorUserId, CancellationToken ct)
    {
        var bucket = await ProposeAsync(ct);
        if (bucket.HighConfidence.Count == 0) return 0;

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var userIds = bucket.HighConfidence.Select(p => p.UserId).Distinct().ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToListAsync(ct);
        var usersById = users.ToDictionary(u => u.Id, u => u);

        // Re-check Persons aren't already linked — guard against races.
        var alreadyLinkedPersonIds = await db.Users.AsNoTracking()
            .Where(u => u.PersonId != null)
            .Select(u => u.PersonId!.Value)
            .ToListAsync(ct);
        var alreadyLinkedSet = new HashSet<int>(alreadyLinkedPersonIds);

        var count = 0;
        var skippedPersonAlreadyLinked = 0;
        foreach (var proposal in bucket.HighConfidence)
        {
            if (!usersById.TryGetValue(proposal.UserId, out var user)) continue;
            if (user.PersonId != null) continue; // already linked — no-op
            if (alreadyLinkedSet.Contains(proposal.PersonId))
            {
                // Person is already linked to a different User — skip this proposal
                // rather than creating a duplicate PersonId link.
                skippedPersonAlreadyLinked++;
                logger.LogInformation(
                    "AutoLink: skipping proposal for user {UserId} → person {PersonId} because the person is already linked to another account.",
                    proposal.UserId, proposal.PersonId);
                continue;
            }

            user.PersonId = proposal.PersonId;
            alreadyLinkedSet.Add(proposal.PersonId);

            db.AuditLogs.Add(new AuditLog
            {
                EntityType = nameof(ApplicationUser),
                EntityId = user.Id,
                Action = "LinkAccount",
                ActorUserId = actorUserId,
                CreatedAtUtc = nowUtc,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    PersonId = proposal.PersonId,
                    PersonName = proposal.PersonFullName,
                    Signal = proposal.Signal.ToString(),
                    Automatic = true
                })
            });

            count++;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Auto-linked {Count} accounts, skipped {Skipped} proposals whose target person was already linked (actor {ActorId}).",
            count, skippedPersonAlreadyLinked, actorUserId);
        return count;
    }

    public async Task LinkAsync(string userId, int personId, string actorUserId, CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            logger.LogDebug("LinkAsync: user {UserId} not found — no-op.", userId);
            return;
        }

        if (user.PersonId != null)
        {
            logger.LogDebug("LinkAsync: user {UserId} already linked to person {PersonId} — no-op.",
                userId, user.PersonId);
            return;
        }

        // Use IgnoreQueryFilters to also catch soft-deleted rows and bail correctly.
        var person = await db.People.AsNoTracking()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(p => p.Id == personId, ct);
        if (person is null || person.IsDeleted)
        {
            logger.LogDebug("LinkAsync: person {PersonId} not found or deleted — no-op.", personId);
            return;
        }

        var personAlreadyLinked = await db.Users.AsNoTracking()
            .AnyAsync(u => u.PersonId == personId && u.Id != userId, ct);
        if (personAlreadyLinked)
        {
            // Surface the conflict so the admin UI can show a meaningful error instead of
            // silently dropping the link (or worse, letting a duplicate through, which then
            // crashes /organizace/role).
            logger.LogWarning(
                "LinkAsync: refused — person {PersonId} is already linked to another user (attempted by actor {ActorId} for user {UserId}).",
                personId, actorUserId, userId);
            throw new InvalidOperationException(
                "Another account is already linked to this Person. Unlink the existing link first.");
        }

        user.PersonId = personId;

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(ApplicationUser),
            EntityId = user.Id,
            Action = "LinkAccount",
            ActorUserId = actorUserId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                PersonId = personId,
                PersonName = FullName(person.FirstName, person.LastName),
                Signal = "Manual",
                Automatic = false
            })
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Manually linked user {UserId} to person {PersonId} (actor {ActorId}).",
            userId, personId, actorUserId);
    }

    public async Task UnlinkAsync(string userId, string actorUserId, CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.PersonId is null)
        {
            logger.LogDebug("UnlinkAsync: user {UserId} not found or already unlinked — no-op.", userId);
            return;
        }

        var previousPersonId = user.PersonId.Value;
        var person = await db.People.AsNoTracking()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(p => p.Id == previousPersonId, ct);

        user.PersonId = null;

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(ApplicationUser),
            EntityId = user.Id,
            Action = "UnlinkAccount",
            ActorUserId = actorUserId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                PersonId = previousPersonId,
                PersonName = person is null ? null : FullName(person.FirstName, person.LastName)
            })
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Unlinked user {UserId} from person {PersonId} (actor {ActorId}).",
            userId, previousPersonId, actorUserId);
    }

    public async Task<IReadOnlyList<AlreadyLinkedView>> ListLinkedAsync(CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var linked = await db.Users.AsNoTracking()
            .Where(u => u.PersonId != null)
            .Select(u => new
            {
                u.Id,
                Email = u.Email ?? "",
                u.DisplayName,
                PersonId = u.PersonId!.Value
            })
            .ToListAsync(ct);

        if (linked.Count == 0) return Array.Empty<AlreadyLinkedView>();

        var personIds = linked.Select(x => x.PersonId).Distinct().ToList();
        var persons = await db.People.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(p => personIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FirstName, p.LastName })
            .ToListAsync(ct);
        var personsById = persons.ToDictionary(p => p.Id, p => p);

        // Pull the most recent LinkAccount audit entry for each user for LinkedAtUtc.
        var userIds = linked.Select(x => x.Id).ToList();
        var linkAuditDates = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(ApplicationUser)
                        && a.Action == "LinkAccount"
                        && userIds.Contains(a.EntityId))
            .GroupBy(a => a.EntityId)
            .Select(g => new { UserId = g.Key, LinkedAt = g.Max(x => x.CreatedAtUtc) })
            .ToListAsync(ct);
        var linkedAtByUser = linkAuditDates.ToDictionary(x => x.UserId, x => x.LinkedAt);

        return linked
            .Select(x =>
            {
                var personName = personsById.TryGetValue(x.PersonId, out var p)
                    ? FullName(p.FirstName, p.LastName)
                    : $"[Osoba #{x.PersonId}]";
                DateTimeOffset? linkedAt = linkedAtByUser.TryGetValue(x.Id, out var dt)
                    ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc))
                    : null;
                return new AlreadyLinkedView(x.Id, x.Email, x.DisplayName, x.PersonId, personName, linkedAt);
            })
            .OrderBy(x => x.UserEmail, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<UnlinkedPersonView>> ListUnlinkedPersonsAsync(CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var linkedPersonIds = await db.Users.AsNoTracking()
            .Where(u => u.PersonId != null)
            .Select(u => u.PersonId!.Value)
            .ToListAsync(ct);
        var linkedSet = new HashSet<int>(linkedPersonIds);

        var persons = await db.People.AsNoTracking()
            .Select(p => new UnlinkedPersonView(p.Id, p.FirstName + " " + p.LastName, p.BirthYear, p.Email))
            .ToListAsync(ct);

        return persons
            .Where(p => !linkedSet.Contains(p.PersonId))
            .OrderBy(p => p.PersonFullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<UserPickerResult>> SearchUsersAsync(
        string query, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return Array.Empty<UserPickerResult>();

        if (limit <= 0) limit = 10;

        var term = query.Trim().ToUpperInvariant();
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var primaryMatches = await db.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail!.Contains(term) || u.DisplayName.ToUpper().Contains(term))
            .Select(u => u.Id)
            .Take(limit * 2)
            .ToListAsync(ct);

        var alternateMatches = await db.UserEmails.AsNoTracking()
            .Where(ue => ue.NormalizedEmail.Contains(term))
            .Select(ue => ue.UserId)
            .Distinct()
            .Take(limit * 2)
            .ToListAsync(ct);

        var allUserIds = primaryMatches.Union(alternateMatches).Distinct().ToList();
        if (allUserIds.Count == 0) return Array.Empty<UserPickerResult>();

        var results = await db.Users.AsNoTracking()
            .Where(u => allUserIds.Contains(u.Id))
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Email)
            .Take(limit)
            .Select(u => new UserPickerResult(u.Id, u.Email ?? "", u.DisplayName))
            .ToListAsync(ct);

        return results;
    }

    // ---- helpers ----

    private static string FullName(string first, string last)
    {
        first = (first ?? "").Trim();
        last = (last ?? "").Trim();
        return string.Join(" ", new[] { first, last }.Where(s => s.Length > 0));
    }

    private static (string First, string Last) SplitFullName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return ("", "");
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return ("", "");
        if (parts.Length == 1) return (parts[0], "");
        // first token is first name, remainder is last name (handles compound surnames).
        var first = parts[0];
        var last = string.Join(" ", parts.Skip(1));
        return (first, last);
    }

    /// <summary>
    /// Trims, lowercases (invariant), and strips combining diacritic marks so
    /// "Tomáš" and "tomas" compare equal. Mirrors the helper used in
    /// <see cref="Features.Integration.IntegrationApiEndpoints"/>.
    /// </summary>
    private static string NormalizeForNameCompare(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";

        var trimmed = s.Trim().ToLowerInvariant();
        var decomposed = trimmed.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Standard iterative Levenshtein distance with two rows. Returns edit distance
    /// between two already-normalized strings.
    /// </summary>
    private static int LevenshteinDistance(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }

    private sealed record UnlinkedUser(string UserId, string Email, string NormalizedEmail, string? DisplayName);

    private sealed record UnlinkedPerson(int PersonId, string FirstName, string LastName, string? Email);

    private sealed record SubmissionRow(
        int Id,
        string PrimaryEmail,
        string PrimaryContactName,
        List<AdultAttendee> AdultAttendees);

    private sealed record AdultAttendee(int PersonId, string FirstName, string LastName);
}
