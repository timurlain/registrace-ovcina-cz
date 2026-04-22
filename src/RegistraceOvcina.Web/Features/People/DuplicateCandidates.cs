namespace RegistraceOvcina.Web.Features.People;

public enum DuplicateMatchReason
{
    ExactNameBirthYearWithin1,
    DiminutiveNameBirthYearWithin1,
    FuzzyNameBirthYearWithin1,
    ExactNameSameBirthYear,
    SharedPhoneOrEmail
}

public sealed record PersonSummary(
    int Id,
    string FullName,
    int BirthYear,
    string? Email,
    string? Phone,
    int RegistrationCount,
    bool HasLinkedUser,
    int CharacterCount);

public sealed record DuplicateCandidatePair(
    PersonSummary Left,
    PersonSummary Right,
    int ConfidenceScore,
    DuplicateMatchReason Reason);

/// <summary>
/// Internal lightweight projection we pull from the DB before pairwise analysis — just the raw
/// fields plus pre-computed counts so we don't need the full Person/Registration graph in memory.
/// </summary>
internal sealed record DuplicateCandidateSource(
    int Id,
    string FirstName,
    string LastName,
    int BirthYear,
    string? Email,
    string? Phone,
    int RegistrationCount,
    bool HasLinkedUser,
    int CharacterCount)
{
    public string NormalizedFirstName { get; } = PersonIdentityNormalizer.NormalizeComparisonText(FirstName);
    public string NormalizedLastName { get; } = PersonIdentityNormalizer.NormalizeComparisonText(LastName);
    public string NormalizedEmail { get; } = PersonIdentityNormalizer.NormalizeEmail(Email);
    public string NormalizedPhone { get; } = PersonIdentityNormalizer.NormalizePhone(Phone);

    public PersonSummary ToSummary() => new(
        Id,
        $"{FirstName} {LastName}".Trim(),
        BirthYear,
        Email,
        Phone,
        RegistrationCount,
        HasLinkedUser,
        CharacterCount);
}

internal static class DuplicateCandidateFinder
{
    /// <summary>
    /// Runs the matching rules on a pre-loaded set of Person projections and returns
    /// the de-duplicated, score-ranked candidate pairs with score ≥ 80.
    /// </summary>
    public static IReadOnlyList<DuplicateCandidatePair> Find(IReadOnlyList<DuplicateCandidateSource> sources)
    {
        // Keyed by canonical (minId, maxId). For each pair we keep the best (highest) scoring reason.
        var byPair = new Dictionary<(int, int), (int Score, DuplicateMatchReason Reason)>();

        void Record(int aId, int bId, int score, DuplicateMatchReason reason)
        {
            var key = aId < bId ? (aId, bId) : (bId, aId);
            if (!byPair.TryGetValue(key, out var existing) || score > existing.Score)
            {
                byPair[key] = (score, reason);
            }
        }

        // --- Name-based rules: group by normalized LastName, then pairwise within bucket. ---
        var byLastName = sources
            .Where(s => !string.IsNullOrEmpty(s.NormalizedLastName))
            .GroupBy(s => s.NormalizedLastName);

        foreach (var bucket in byLastName)
        {
            var list = bucket.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                for (var j = i + 1; j < list.Count; j++)
                {
                    var a = list[i];
                    var b = list[j];
                    var yearDelta = Math.Abs(a.BirthYear - b.BirthYear);

                    var fnExact = !string.IsNullOrEmpty(a.NormalizedFirstName)
                                  && a.NormalizedFirstName == b.NormalizedFirstName;
                    var fnDiminutive = !fnExact
                                       && !string.IsNullOrEmpty(a.NormalizedFirstName)
                                       && !string.IsNullOrEmpty(b.NormalizedFirstName)
                                       && CzechDiminutives.AreEquivalent(a.NormalizedFirstName, b.NormalizedFirstName);
                    var fnFuzzy = !fnExact && !fnDiminutive
                                  && !string.IsNullOrEmpty(a.NormalizedFirstName)
                                  && !string.IsNullOrEmpty(b.NormalizedFirstName)
                                  && a.NormalizedFirstName.Length >= 3
                                  && b.NormalizedFirstName.Length >= 3
                                  && Levenshtein(a.NormalizedFirstName, b.NormalizedFirstName) <= 2;

                    if (fnExact && yearDelta == 0)
                    {
                        Record(a.Id, b.Id, 80, DuplicateMatchReason.ExactNameSameBirthYear);
                    }
                    if (fnExact && yearDelta <= 1)
                    {
                        // yearDelta==0 also hits this branch — the higher score (95) wins over 80.
                        Record(a.Id, b.Id, 95, DuplicateMatchReason.ExactNameBirthYearWithin1);
                    }
                    else if (fnDiminutive && yearDelta <= 1)
                    {
                        Record(a.Id, b.Id, 90, DuplicateMatchReason.DiminutiveNameBirthYearWithin1);
                    }
                    else if (fnFuzzy && yearDelta <= 1)
                    {
                        Record(a.Id, b.Id, 85, DuplicateMatchReason.FuzzyNameBirthYearWithin1);
                    }
                }
            }
        }

        // --- SharedPhoneOrEmail: group by normalized contact, any group with >1 yields pairs. ---
        foreach (var group in sources
                     .Where(s => !string.IsNullOrEmpty(s.NormalizedEmail))
                     .GroupBy(s => s.NormalizedEmail)
                     .Where(g => g.Count() > 1))
        {
            PairsWithin(group.ToList(), 88, DuplicateMatchReason.SharedPhoneOrEmail, Record);
        }

        foreach (var group in sources
                     .Where(s => !string.IsNullOrEmpty(s.NormalizedPhone))
                     .GroupBy(s => s.NormalizedPhone)
                     .Where(g => g.Count() > 1))
        {
            PairsWithin(group.ToList(), 88, DuplicateMatchReason.SharedPhoneOrEmail, Record);
        }

        // Materialize pairs, filter score≥80 (all rules already emit ≥80 but be defensive), sort.
        var byId = sources.ToDictionary(s => s.Id);
        return byPair
            .Where(kv => kv.Value.Score >= 80)
            .Select(kv => new DuplicateCandidatePair(
                byId[kv.Key.Item1].ToSummary(),
                byId[kv.Key.Item2].ToSummary(),
                kv.Value.Score,
                kv.Value.Reason))
            .OrderByDescending(p => p.ConfidenceScore)
            .ThenBy(p => p.Left.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Right.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void PairsWithin(
        List<DuplicateCandidateSource> group,
        int score,
        DuplicateMatchReason reason,
        Action<int, int, int, DuplicateMatchReason> record)
    {
        for (var i = 0; i < group.Count; i++)
        {
            for (var j = i + 1; j < group.Count; j++)
            {
                record(group[i].Id, group[j].Id, score, reason);
            }
        }
    }

    /// <summary>
    /// Iterative Levenshtein distance with two rolling rows. ASCII-only inputs (normalized).
    /// </summary>
    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

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
}
