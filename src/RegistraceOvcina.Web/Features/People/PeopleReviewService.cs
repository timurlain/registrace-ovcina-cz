using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.People;

public sealed class PeopleReviewService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    TimeProvider timeProvider)
{
    public async Task<PeopleReviewListPageModel> GetListAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var peopleQuery = db.People.AsNoTracking();

        // Push coarse text filter into SQL before materializing
        if (!string.IsNullOrWhiteSpace(query))
        {
            var trimmed = query.Trim();
            peopleQuery = peopleQuery.Where(x =>
                (x.FirstName + " " + x.LastName).Contains(trimmed)
                || (x.Email != null && x.Email.Contains(trimmed))
                || (x.Phone != null && x.Phone.Contains(trimmed)));
        }

        var people = await peopleQuery
            .Select(x => new PersonListProjection(
                x.Id,
                x.FirstName,
                x.LastName,
                x.BirthYear,
                x.Email,
                x.Phone,
                x.Registrations.Count,
                x.Registrations
                    .OrderByDescending(r => r.Submission.Game.StartsAtUtc)
                    .Select(r => (DateTime?)r.Submission.Game.StartsAtUtc)
                    .FirstOrDefault(),
                x.Registrations
                    .OrderByDescending(r => r.Submission.Game.StartsAtUtc)
                    .Select(r => r.Submission.Game.Name)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        var linkedUsers = await db.Users
            .AsNoTracking()
            .Where(x => x.PersonId != null)
            .ToListAsync(cancellationToken);

        var linkedUserLookup = linkedUsers
            .Where(x => x.PersonId is not null && !IsImportOnlyUser(x.Email))
            .GroupBy(x => x.PersonId!.Value)
            .ToDictionary(
                x => x.Key,
                x => x
                    .OrderByDescending(user => user.IsActive)
                    .ThenBy(user => user.Email)
                    .Select(user => user.Email ?? user.UserName ?? user.Id)
                    .FirstOrDefault());

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalizedQuery = PersonIdentityNormalizer.NormalizeComparisonText(query);
            var normalizedPhone = PersonIdentityNormalizer.NormalizePhone(query);

            people = people
                .Where(x =>
                    PersonIdentityNormalizer.NormalizeComparisonText($"{x.FirstName} {x.LastName}").Contains(normalizedQuery, StringComparison.Ordinal)
                    || PersonIdentityNormalizer.NormalizeEmail(x.Email).Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(normalizedPhone)
                        && PersonIdentityNormalizer.NormalizePhone(x.Phone).Contains(normalizedPhone, StringComparison.Ordinal)))
                .ToList();
        }

        var items = people
            .OrderBy(x => x.LastName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.FirstName, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new PeopleReviewListItem(
                x.Id,
                $"{x.FirstName} {x.LastName}",
                x.BirthYear,
                x.Email,
                x.Phone,
                x.RegistrationCount,
                x.LastSeenAtUtc,
                x.LastSeenGameName,
                linkedUserLookup.GetValueOrDefault(x.Id)))
            .ToList();

        return new PeopleReviewListPageModel(items, query?.Trim());
    }

    public async Task<PersonReviewDetailPageModel?> GetDetailAsync(
        int personId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var person = await db.People
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == personId, cancellationToken);

        if (person is null)
        {
            return null;
        }

        var registrationHistory = await db.Registrations
            .AsNoTracking()
            .Where(x => x.PersonId == personId)
            .OrderByDescending(x => x.Submission.Game.StartsAtUtc)
            .Select(x => new PersonRegistrationHistoryItem(
                x.Id,
                x.SubmissionId,
                x.Submission.Game.Name,
                x.Submission.Game.StartsAtUtc,
                x.AttendeeType,
                x.Status,
                x.CharacterName,
                x.PreferredKingdom != null ? x.PreferredKingdom.DisplayName : null,
                x.Submission.PrimaryContactName))
            .ToListAsync(cancellationToken);

        var linkedUsers = await db.Users
            .AsNoTracking()
            .Where(x => x.PersonId == personId)
            .ToListAsync(cancellationToken);

        var linkedAccounts = linkedUsers
            .Where(x => !IsImportOnlyUser(x.Email))
            .Select(x => new LinkedAccountItem(
                x.Id,
                BuildUserLabel(x),
                x.Email ?? x.UserName ?? x.Id,
                x.IsActive,
                x.LastLoginAtUtc))
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Email, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var linkableAccounts = await GetLinkableAccountsAsync(db, person, cancellationToken);
        var candidates = await GetCandidatePeopleAsync(db, person, cancellationToken);

        var linkedMessages = await db.EmailMessages
            .AsNoTracking()
            .Where(x => x.LinkedPersonId == personId)
            .OrderByDescending(x => x.ReceivedAtUtc ?? x.SentAtUtc)
            .Take(12)
            .Select(x => new LinkedEmailMessageItem(
                x.Id,
                string.IsNullOrWhiteSpace(x.Subject) ? "(bez předmětu)" : x.Subject,
                x.From,
                x.ReceivedAtUtc,
                x.SentAtUtc,
                x.Direction))
            .ToListAsync(cancellationToken);

        var notes = await db.OrganizerNotes
            .AsNoTracking()
            .Where(x => x.PersonId == personId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var authorLookup = await db.Users
            .AsNoTracking()
            .Where(x => notes.Select(note => note.AuthorUserId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, BuildUserLabel, cancellationToken);

        var organizerNotes = notes
            .Select(x => new PersonOrganizerNoteItem(
                x.Id,
                x.Note,
                x.CreatedAtUtc,
                authorLookup.GetValueOrDefault(x.AuthorUserId, x.AuthorUserId)))
            .ToList();

        return new PersonReviewDetailPageModel(
            person.Id,
            person.FirstName,
            person.LastName,
            $"{person.FirstName} {person.LastName}",
            person.BirthYear,
            person.Email,
            person.Phone,
            linkedAccounts,
            linkableAccounts,
            candidates,
            registrationHistory,
            linkedMessages,
            organizerNotes);
    }

    public async Task LinkUserAsync(
        int personId,
        string targetUserId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetUserId))
        {
            throw new ValidationException("Vyberte účet, který chcete propojit.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var person = await db.People.FirstOrDefaultAsync(x => x.Id == personId, cancellationToken)
            ?? throw new ValidationException("Osoba nebyla nalezena.");
        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == targetUserId, cancellationToken)
            ?? throw new ValidationException("Účet nebyl nalezen.");

        if (IsImportOnlyUser(user.Email))
        {
            throw new ValidationException("Importní účet nelze propojit ručně.");
        }

        if (user.PersonId == personId)
        {
            return;
        }

        if (user.PersonId is not null)
        {
            throw new ValidationException("Tento účet už je propojený s jinou osobou.");
        }

        var existingLinkedUser = await db.Users
            .Where(x => x.PersonId == personId && x.Id != targetUserId)
            .ToListAsync(cancellationToken);

        if (existingLinkedUser.Any(x => !IsImportOnlyUser(x.Email)))
        {
            throw new ValidationException("Tato osoba už má propojený jiný účet.");
        }

        user.PersonId = personId;

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(Person),
            EntityId = personId.ToString(),
            Action = "PersonUserLinked",
            ActorUserId = actorUserId,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            DetailsJson = JsonSerializer.Serialize(new
            {
                UserId = user.Id,
                user.Email
            })
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UnlinkUserAsync(
        int personId,
        string targetUserId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetUserId))
        {
            throw new ValidationException("Vyberte účet, který chcete odpojit.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == targetUserId, cancellationToken)
            ?? throw new ValidationException("Účet nebyl nalezen.");

        if (user.PersonId != personId)
        {
            throw new ValidationException("Účet není propojený s touto osobou.");
        }

        user.PersonId = null;

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(Person),
            EntityId = personId.ToString(),
            Action = "PersonUserUnlinked",
            ActorUserId = actorUserId,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            DetailsJson = JsonSerializer.Serialize(new
            {
                UserId = user.Id,
                user.Email
            })
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MergeAsync(
        int canonicalPersonId,
        int duplicatePersonId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (canonicalPersonId == duplicatePersonId)
        {
            throw new ValidationException("Osobu nelze sloučit samu do sebe.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var people = await db.People
            .Where(x => x.Id == canonicalPersonId || x.Id == duplicatePersonId)
            .ToListAsync(cancellationToken);

        var canonical = people.SingleOrDefault(x => x.Id == canonicalPersonId)
            ?? throw new ValidationException("Cílová osoba nebyla nalezena.");
        var duplicate = people.SingleOrDefault(x => x.Id == duplicatePersonId)
            ?? throw new ValidationException("Duplicitní osoba nebyla nalezena.");

        var overlappingSubmissions = await db.Registrations
            .Where(x => x.PersonId == canonicalPersonId || x.PersonId == duplicatePersonId)
            .GroupBy(x => x.SubmissionId)
            .Where(x => x.Select(item => item.PersonId).Distinct().Count() > 1)
            .Select(x => x.Key)
            .ToListAsync(cancellationToken);

        if (overlappingSubmissions.Count > 0)
        {
            throw new ValidationException("Obě osoby už mají účast ve stejné přihlášce. Sloučení by nebylo bezpečné.");
        }

        var linkedUsers = await db.Users
            .Where(x => x.PersonId == canonicalPersonId || x.PersonId == duplicatePersonId)
            .ToListAsync(cancellationToken);

        var linkedRealUserIds = linkedUsers
            .Where(x => x.IsActive && !IsImportOnlyUser(x.Email))
            .Select(x => x.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (linkedRealUserIds.Count > 1)
        {
            throw new ValidationException("Obě osoby už mají propojené různé reálné účty. Sloučení zatím proveďte ručně.");
        }

        var registrations = await db.Registrations
            .Where(x => x.PersonId == duplicatePersonId)
            .ToListAsync(cancellationToken);
        foreach (var registration in registrations)
        {
            registration.PersonId = canonicalPersonId;
        }

        var organizerNotes = await db.OrganizerNotes
            .Where(x => x.PersonId == duplicatePersonId)
            .ToListAsync(cancellationToken);
        foreach (var note in organizerNotes)
        {
            note.PersonId = canonicalPersonId;
        }

        var emailMessages = await db.EmailMessages
            .Where(x => x.LinkedPersonId == duplicatePersonId)
            .ToListAsync(cancellationToken);
        foreach (var message in emailMessages)
        {
            message.LinkedPersonId = canonicalPersonId;
        }

        var importRows = await db.HistoricalImportRows
            .Where(x => x.LinkedPersonId == duplicatePersonId)
            .ToListAsync(cancellationToken);
        foreach (var row in importRows)
        {
            row.LinkedPersonId = canonicalPersonId;
        }

        foreach (var user in linkedUsers.Where(x => x.PersonId == duplicatePersonId))
        {
            user.PersonId = canonicalPersonId;
        }

        var canonicalCharacters = await db.Characters
            .Include(x => x.Appearances)
            .Where(x => x.PersonId == canonicalPersonId)
            .ToListAsync(cancellationToken);
        var duplicateCharacters = await db.Characters
            .Include(x => x.Appearances)
            .Where(x => x.PersonId == duplicatePersonId)
            .ToListAsync(cancellationToken);

        var canonicalCharacterLookup = canonicalCharacters.ToDictionary(
            x => PersonIdentityNormalizer.NormalizeComparisonText(x.Name),
            x => x,
            StringComparer.Ordinal);

        foreach (var duplicateCharacter in duplicateCharacters)
        {
            var normalizedCharacterName = PersonIdentityNormalizer.NormalizeComparisonText(duplicateCharacter.Name);
            if (canonicalCharacterLookup.TryGetValue(normalizedCharacterName, out var canonicalCharacter))
            {
                foreach (var duplicateAppearance in duplicateCharacter.Appearances.ToList())
                {
                    var existingAppearance = canonicalCharacter.Appearances
                        .SingleOrDefault(x => x.GameId == duplicateAppearance.GameId);

                    if (existingAppearance is null)
                    {
                        duplicateAppearance.CharacterId = canonicalCharacter.Id;
                        canonicalCharacter.Appearances.Add(duplicateAppearance);
                    }
                    else
                    {
                        existingAppearance.RegistrationId ??= duplicateAppearance.RegistrationId;
                        existingAppearance.AssignedKingdomId ??= duplicateAppearance.AssignedKingdomId;
                        existingAppearance.LevelReached ??= duplicateAppearance.LevelReached;
                        existingAppearance.Notes = AppendUniqueText(existingAppearance.Notes, duplicateAppearance.Notes);
                        db.CharacterAppearances.Remove(duplicateAppearance);
                    }
                }

                duplicateCharacter.IsDeleted = true;
            }
            else
            {
                duplicateCharacter.PersonId = canonicalPersonId;
                canonicalCharacterLookup[normalizedCharacterName] = duplicateCharacter;
            }
        }

        if (string.IsNullOrWhiteSpace(canonical.Email) && !string.IsNullOrWhiteSpace(duplicate.Email))
        {
            canonical.Email = duplicate.Email.Trim();
        }

        if (string.IsNullOrWhiteSpace(canonical.Phone) && !string.IsNullOrWhiteSpace(duplicate.Phone))
        {
            canonical.Phone = duplicate.Phone.Trim();
        }

        canonical.Notes = AppendUniqueText(canonical.Notes, duplicate.Notes);
        canonical.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        duplicate.IsDeleted = true;
        duplicate.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(Person),
            EntityId = canonicalPersonId.ToString(),
            Action = "PersonMerged",
            ActorUserId = actorUserId,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            DetailsJson = JsonSerializer.Serialize(new
            {
                DuplicatePersonId = duplicatePersonId,
                MovedRegistrationCount = registrations.Count,
                MovedNoteCount = organizerNotes.Count,
                MovedMessageCount = emailMessages.Count
            })
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<LinkableAccountItem>> GetLinkableAccountsAsync(
        ApplicationDbContext db,
        Person person,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(person.Email))
        {
            return [];
        }

        var normalizedEmail = person.Email.Trim().ToUpperInvariant();
        var candidates = await db.Users
            .AsNoTracking()
            .Where(x => x.NormalizedEmail == normalizedEmail)
            .ToListAsync(cancellationToken);

        return candidates
            .Where(x => x.PersonId is null && !IsImportOnlyUser(x.Email))
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Email, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new LinkableAccountItem(
                x.Id,
                BuildUserLabel(x),
                x.Email ?? x.UserName ?? x.Id,
                x.IsActive,
                x.LastLoginAtUtc))
            .ToList();
    }

    private async Task<List<PersonMatchCandidateItem>> GetCandidatePeopleAsync(
        ApplicationDbContext db,
        Person person,
        CancellationToken cancellationToken)
    {
        var normalizedFirstName = PersonIdentityNormalizer.NormalizeComparisonText(person.FirstName);
        var normalizedLastName = PersonIdentityNormalizer.NormalizeComparisonText(person.LastName);
        var normalizedEmail = PersonIdentityNormalizer.NormalizeEmail(person.Email);
        var normalizedPhone = PersonIdentityNormalizer.NormalizePhone(person.Phone);

        var candidates = await db.People
            .AsNoTracking()
            .Where(x => x.Id != person.Id && x.BirthYear == person.BirthYear)
            .Select(x => new PersonCandidateProjection(
                x.Id,
                x.FirstName,
                x.LastName,
                x.BirthYear,
                x.Email,
                x.Phone,
                x.Registrations.Count,
                x.Registrations
                    .OrderByDescending(r => r.Submission.Game.StartsAtUtc)
                    .Select(r => (DateTime?)r.Submission.Game.StartsAtUtc)
                    .FirstOrDefault(),
                x.Registrations
                    .OrderByDescending(r => r.Submission.Game.StartsAtUtc)
                    .Select(r => r.Submission.Game.Name)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return candidates
            .Select(x =>
            {
                var reasons = new List<string>();
                if (PersonIdentityNormalizer.NormalizeComparisonText(x.FirstName) == normalizedFirstName
                    && PersonIdentityNormalizer.NormalizeComparisonText(x.LastName) == normalizedLastName)
                {
                    reasons.Add("Stejné jméno a ročník");
                }

                if (!string.IsNullOrWhiteSpace(normalizedEmail)
                    && PersonIdentityNormalizer.NormalizeEmail(x.Email) == normalizedEmail)
                {
                    reasons.Add("Stejný e-mail");
                }

                if (!string.IsNullOrWhiteSpace(normalizedPhone)
                    && PersonIdentityNormalizer.NormalizePhone(x.Phone) == normalizedPhone)
                {
                    reasons.Add("Stejný telefon");
                }

                return reasons.Count == 0
                    ? null
                    : new PersonMatchCandidateItem(
                        x.Id,
                        $"{x.FirstName} {x.LastName}",
                        x.BirthYear,
                        x.Email,
                        x.Phone,
                        reasons,
                        x.RegistrationCount,
                        x.LastSeenAtUtc,
                        x.LastSeenGameName);
            })
            .Where(x => x is not null)
            .OrderByDescending(x => x!.MatchReasons.Count)
            .ThenByDescending(x => x!.LastSeenAtUtc)
            .ThenBy(x => x!.FullName, StringComparer.CurrentCultureIgnoreCase)
            .Cast<PersonMatchCandidateItem>()
            .ToList();
    }

    private static string BuildUserLabel(ApplicationUser user) =>
        string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.Email ?? user.UserName ?? user.Id
            : user.DisplayName;

    private static bool IsImportOnlyUser(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && email.EndsWith("@import.ovcina.local", StringComparison.OrdinalIgnoreCase);

    private static string? AppendUniqueText(string? existing, string? incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming))
        {
            return existing;
        }

        if (string.IsNullOrWhiteSpace(existing))
        {
            return incoming.Trim();
        }

        var trimmedIncoming = incoming.Trim();
        if (existing.Contains(trimmedIncoming, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        return $"{existing.Trim()}{Environment.NewLine}{trimmedIncoming}";
    }

    private sealed record PersonListProjection(
        int Id,
        string FirstName,
        string LastName,
        int BirthYear,
        string? Email,
        string? Phone,
        int RegistrationCount,
        DateTime? LastSeenAtUtc,
        string? LastSeenGameName);

    private sealed record PersonCandidateProjection(
        int Id,
        string FirstName,
        string LastName,
        int BirthYear,
        string? Email,
        string? Phone,
        int RegistrationCount,
        DateTime? LastSeenAtUtc,
        string? LastSeenGameName);
}

public sealed record PeopleReviewListPageModel(
    IReadOnlyList<PeopleReviewListItem> People,
    string? Query);

public sealed record PeopleReviewListItem(
    int Id,
    string FullName,
    int BirthYear,
    string? Email,
    string? Phone,
    int RegistrationCount,
    DateTime? LastSeenAtUtc,
    string? LastSeenGameName,
    string? LinkedUserEmail);

public sealed record PersonReviewDetailPageModel(
    int Id,
    string FirstName,
    string LastName,
    string FullName,
    int BirthYear,
    string? Email,
    string? Phone,
    IReadOnlyList<LinkedAccountItem> LinkedAccounts,
    IReadOnlyList<LinkableAccountItem> LinkableAccounts,
    IReadOnlyList<PersonMatchCandidateItem> MatchCandidates,
    IReadOnlyList<PersonRegistrationHistoryItem> RegistrationHistory,
    IReadOnlyList<LinkedEmailMessageItem> LinkedMessages,
    IReadOnlyList<PersonOrganizerNoteItem> OrganizerNotes);

public sealed record LinkedAccountItem(
    string Id,
    string DisplayName,
    string Email,
    bool IsActive,
    DateTime? LastLoginAtUtc);

public sealed record LinkableAccountItem(
    string Id,
    string DisplayName,
    string Email,
    bool IsActive,
    DateTime? LastLoginAtUtc);

public sealed record PersonMatchCandidateItem(
    int Id,
    string FullName,
    int BirthYear,
    string? Email,
    string? Phone,
    IReadOnlyList<string> MatchReasons,
    int RegistrationCount,
    DateTime? LastSeenAtUtc,
    string? LastSeenGameName);

public sealed record PersonRegistrationHistoryItem(
    int RegistrationId,
    int SubmissionId,
    string GameName,
    DateTime GameStartsAtUtc,
    AttendeeType AttendeeType,
    RegistrationStatus Status,
    string? CharacterName,
    string? PreferredKingdom,
    string SubmissionContact);

public sealed record LinkedEmailMessageItem(
    int Id,
    string Subject,
    string From,
    DateTime? ReceivedAtUtc,
    DateTime? SentAtUtc,
    EmailDirection Direction);

public sealed record PersonOrganizerNoteItem(
    int Id,
    string Note,
    DateTime CreatedAtUtc,
    string AuthorLabel);
