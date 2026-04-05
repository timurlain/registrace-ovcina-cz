namespace RegistraceOvcina.Web.Infrastructure;

public static class CzechTime
{
    private static readonly string[] CandidateIds = ["Europe/Prague", "Central Europe Standard Time"];

    public static TimeZoneInfo TimeZone { get; } = ResolveTimeZone();

    public static DateTime ToLocal(DateTime utc)
    {
        var normalizedUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, TimeZone);
    }

    public static DateTime ToUtc(DateTime local)
    {
        var unspecifiedLocal = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecifiedLocal, TimeZone);
    }

    public static string Format(DateTime utc) => ToLocal(utc).ToString("d. M. yyyy HH:mm");

    private static TimeZoneInfo ResolveTimeZone()
    {
        foreach (var id in CandidateIds)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Expected — try next candidate (Linux vs Windows IDs differ)
                continue;
            }
            catch (InvalidTimeZoneException)
            {
                // Corrupted TZ data — try next candidate
                continue;
            }
        }

        throw new InvalidOperationException("Europe/Prague time zone is not available on this machine.");
    }
}
