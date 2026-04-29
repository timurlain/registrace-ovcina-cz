using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Roles;

/// <summary>
/// Diagnostic helper for the /admin/role-email-suggestions/{gameId} page.
/// For every adult on a given game whose Person.Email is empty, finds candidate
/// emails from related sources so the organizer can fill in the gaps before
/// blasting Character Prep / pre-game mails through the Pošta UI.
/// </summary>
public sealed class RoleEmailSuggestionService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<List<AdultEmailSuggestion>> BuildAsync(int gameId, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        // Adults on this game whose Person has no email — mirrors the WHERE clause in
        // GameRolesViewService.BuildAdultViewsAsync so the diagnostic stays in sync with
        // what /organizace/role actually shows (Active registrations only).
        var adults = await db.Registrations
            .Where(r => r.Submission.GameId == gameId
                && !r.Submission.IsDeleted
                && r.AttendeeType == AttendeeType.Adult
                && r.Status == RegistrationStatus.Active
                && !r.Person.IsDeleted
                && (r.Person.Email == null || r.Person.Email == ""))
            .OrderBy(r => r.Person.LastName)
            .ThenBy(r => r.Person.FirstName)
            .Select(r => new
            {
                r.PersonId,
                r.Person.FirstName,
                r.Person.LastName,
                r.Submission.GroupName,
                r.Submission.PrimaryEmail
            })
            .ToListAsync(ct);

        if (adults.Count == 0)
        {
            return [];
        }

        var personIds = adults.Select(a => a.PersonId).Distinct().ToList();

        // Channel A: ApplicationUser linked via PersonId (with their primary + alternate emails).
        var linkedUsers = await db.Users
            .Where(u => u.PersonId != null && personIds.Contains(u.PersonId.Value))
            .Select(u => new
            {
                PersonId = u.PersonId!.Value,
                u.Email,
                Alternates = u.AlternateEmails.Select(ae => ae.Email).ToList()
            })
            .ToListAsync(ct);

        var usersByPersonId = linkedUsers
            .GroupBy(u => u.PersonId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Channel B: any other Person sharing this person's last name AND has a non-empty email.
        // We pull by last-name match (ToUpper-safe in Postgres) and filter first-name in memory
        // to avoid building a complex tuple-OR clause.
        // Use Invariant for the in-memory list so it matches in non-en-US server cultures
        // (e.g. Turkish dotted-i). The DB-side `p.LastName.ToUpper()` translates to
        // Postgres UPPER() which is locale-driven by the database collation.
        var lastNamesUpper = adults.Select(a => a.LastName.ToUpperInvariant()).Distinct().ToList();
        var sameLastNamePersons = await db.People
            .Where(p => p.Email != null && p.Email != ""
                && !p.IsDeleted
                && lastNamesUpper.Contains(p.LastName.ToUpper()))
            .Select(p => new { p.Id, p.FirstName, p.LastName, p.Email })
            .ToListAsync(ct);

        var sameNameByName = sameLastNamePersons
            .GroupBy(p => (FN: p.FirstName.ToUpperInvariant(), LN: p.LastName.ToUpperInvariant()))
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<AdultEmailSuggestion>(adults.Count);
        foreach (var a in adults)
        {
            var candidates = new List<EmailCandidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. ApplicationUser linked via PersonId — strongest signal (explicit link).
            if (usersByPersonId.TryGetValue(a.PersonId, out var users))
            {
                foreach (var u in users)
                {
                    if (!string.IsNullOrWhiteSpace(u.Email) && seen.Add(u.Email))
                    {
                        candidates.Add(new EmailCandidate(u.Email, "ApplicationUser link", "Vysoká"));
                    }
                    foreach (var ae in u.Alternates)
                    {
                        if (!string.IsNullOrWhiteSpace(ae) && seen.Add(ae))
                        {
                            candidates.Add(new EmailCandidate(ae, "ApplicationUser alternate", "Vysoká"));
                        }
                    }
                }
            }

            // 2. Submission PrimaryEmail — household contact (likely a parent / partner).
            if (!string.IsNullOrWhiteSpace(a.PrimaryEmail) && seen.Add(a.PrimaryEmail))
            {
                candidates.Add(new EmailCandidate(a.PrimaryEmail, "Submission.PrimaryEmail (rodinný kontakt)", "Střední"));
            }

            // 3. Same-name Person elsewhere with an email — likely a merge candidate.
            var key = (FN: a.FirstName.ToUpperInvariant(), LN: a.LastName.ToUpperInvariant());
            if (sameNameByName.TryGetValue(key, out var samePersons))
            {
                foreach (var sp in samePersons.Where(p => p.Id != a.PersonId))
                {
                    if (!string.IsNullOrWhiteSpace(sp.Email) && seen.Add(sp.Email!))
                    {
                        candidates.Add(new EmailCandidate(sp.Email!, $"Same-name Person #{sp.Id}", "Nízká — ověřit"));
                    }
                }
            }

            result.Add(new AdultEmailSuggestion(
                PersonId: a.PersonId,
                FullName: $"{a.FirstName} {a.LastName}",
                LastName: a.LastName,
                FirstName: a.FirstName,
                GroupName: a.GroupName,
                Candidates: candidates));
        }

        return result;
    }
}

public sealed record AdultEmailSuggestion(
    int PersonId,
    string FullName,
    string LastName,
    string FirstName,
    string? GroupName,
    List<EmailCandidate> Candidates);

public sealed record EmailCandidate(string Email, string Source, string Confidence);
