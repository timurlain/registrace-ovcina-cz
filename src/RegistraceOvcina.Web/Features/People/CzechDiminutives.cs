namespace RegistraceOvcina.Web.Features.People;

// Common Czech first-name diminutives. Each entry groups equivalent first-name forms.
// Matching: two names are diminutive-equivalent if they appear in the same set.
// Values are stored already-normalized (lowercase, diacritics removed), which matches
// PersonIdentityNormalizer.NormalizeComparisonText output.
internal static class CzechDiminutives
{
    public static readonly IReadOnlyList<IReadOnlySet<string>> EquivalenceGroups = new List<HashSet<string>>
    {
        new(StringComparer.Ordinal) { "jan", "honza", "honzik", "jenda" },
        new(StringComparer.Ordinal) { "josef", "pepa", "pepik" },
        new(StringComparer.Ordinal) { "jiri", "jirka", "jirek" },
        new(StringComparer.Ordinal) { "tomas", "tom", "tomek", "tomik" },
        new(StringComparer.Ordinal) { "petr", "petrik", "peta" },
        new(StringComparer.Ordinal) { "pavel", "pavlik", "pavlicek" },
        new(StringComparer.Ordinal) { "martin", "martinek", "marta", "martinko" },
        new(StringComparer.Ordinal) { "karel", "karlik", "kaja" },
        new(StringComparer.Ordinal) { "lukas", "luky", "luki" },
        new(StringComparer.Ordinal) { "david", "davidek", "dave" },
        new(StringComparer.Ordinal) { "stepan", "stepanek" },
        new(StringComparer.Ordinal) { "vojtech", "vojta", "vojtik" },
        new(StringComparer.Ordinal) { "frantisek", "franta", "francek" },
        new(StringComparer.Ordinal) { "marek", "marecek" },
        new(StringComparer.Ordinal) { "matej", "matejek" },
        new(StringComparer.Ordinal) { "ondrej", "ondra", "ondrasek" },
        new(StringComparer.Ordinal) { "radek", "radomir", "radovan" },
        new(StringComparer.Ordinal) { "richard", "risa", "ricky" },
        new(StringComparer.Ordinal) { "roman", "romek" },
        new(StringComparer.Ordinal) { "filip", "filda" },
        new(StringComparer.Ordinal) { "marie", "maruska", "majka", "maja", "marja" },
        new(StringComparer.Ordinal) { "katerina", "katka", "kata", "kacenka", "kacka" },
        new(StringComparer.Ordinal) { "anna", "anicka", "anca" },
        new(StringComparer.Ordinal) { "eva", "evicka", "evuska" },
        new(StringComparer.Ordinal) { "vera", "veruska", "verca" },
        new(StringComparer.Ordinal) { "barbora", "bara", "baruska" },
        new(StringComparer.Ordinal) { "tereza", "terka", "terezka" },
        new(StringComparer.Ordinal) { "lucie", "lucka", "lucinka" },
        new(StringComparer.Ordinal) { "hana", "hanka", "haninka" },
        new(StringComparer.Ordinal) { "jana", "janinka", "janca" },
        new(StringComparer.Ordinal) { "veronika", "verca", "vercka" },
        new(StringComparer.Ordinal) { "natalie", "natalka" },
        new(StringComparer.Ordinal) { "stanislav", "standa", "stanicek" },
        new(StringComparer.Ordinal) { "stanislava", "stanicka", "standa" }
    };

    public static bool AreEquivalent(string normA, string normB)
    {
        if (normA == normB) return true;
        foreach (var group in EquivalenceGroups)
        {
            if (group.Contains(normA) && group.Contains(normB)) return true;
        }
        return false;
    }
}
