using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace RegistraceOvcina.Web.Features.HistoricalImport;

public sealed class HistoricalImportInput
{
    private const string DateTimeLocalFormat = "yyyy-MM-ddTHH:mm";

    [Required(ErrorMessage = "Zadejte popis dávky importu.")]
    [StringLength(200)]
    public string Label { get; set; } = "";

    [Required(ErrorMessage = "Zadejte název historické hry.")]
    [StringLength(200)]
    public string GameName { get; set; } = "";

    [Required(ErrorMessage = "Zadejte začátek historické hry.")]
    public string StartsAtLocalText { get; set; } = "";

    [Required(ErrorMessage = "Zadejte konec historické hry.")]
    public string EndsAtLocalText { get; set; } = "";

    public HistoricalImportCommand ToCommand(string sourceFileName) =>
        new(
            Label.Trim(),
            GameName.Trim(),
            ParseDateTime(StartsAtLocalText, "Začátek historické hry"),
            ParseDateTime(EndsAtLocalText, "Konec historické hry"),
            sourceFileName);

    public static HistoricalImportInput CreateDefaults()
    {
        var year = DateTime.Today.Year - 1;
        var startsAtLocal = new DateTime(year, 5, 1, 8, 0, 0, DateTimeKind.Unspecified);
        var endsAtLocal = startsAtLocal.AddDays(1).AddHours(8);

        return new HistoricalImportInput
        {
            Label = $"Historie {year}",
            StartsAtLocalText = FormatDateTime(startsAtLocal),
            EndsAtLocalText = FormatDateTime(endsAtLocal)
        };
    }

    public static string FormatDateTime(DateTime value) =>
        value.ToString(DateTimeLocalFormat, CultureInfo.InvariantCulture);

    private static DateTime ParseDateTime(string rawValue, string fieldName)
    {
        if (DateTime.TryParseExact(
                rawValue,
                DateTimeLocalFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        }

        throw new ValidationException($"{fieldName} musí mít platný formát data a času.");
    }
}

public sealed record HistoricalImportCommand(
    string Label,
    string GameName,
    DateTime StartsAtLocal,
    DateTime EndsAtLocal,
    string SourceFileName);
