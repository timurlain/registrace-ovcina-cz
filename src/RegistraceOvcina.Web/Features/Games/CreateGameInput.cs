using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace RegistraceOvcina.Web.Features.Games;

public sealed class CreateGameInput : IValidatableObject
{
    private const string DateTimeLocalFormat = "yyyy-MM-ddTHH:mm";

    [Required(ErrorMessage = "Vyplňte název hry.")]
    public string Name { get; set; } = "";

    public string? Description { get; set; }

    [Required(ErrorMessage = "Vyplňte začátek hry.")]
    public string StartsAtLocalText { get; set; } = "";

    [Required(ErrorMessage = "Vyplňte konec hry.")]
    public string EndsAtLocalText { get; set; } = "";

    [Required(ErrorMessage = "Vyplňte uzávěrku registrace.")]
    public string RegistrationClosesAtLocalText { get; set; } = "";

    [Required(ErrorMessage = "Vyplňte uzávěrku jídel.")]
    public string MealOrderingClosesAtLocalText { get; set; } = "";

    [Required(ErrorMessage = "Vyplňte termín platby.")]
    public string PaymentDueAtLocalText { get; set; } = "";

    public string? AssignmentFreezeAtLocalText { get; set; }

    [Range(0, 50000, ErrorMessage = "Cena hráče musí být kladná nebo nulová.")]
    public decimal PlayerBasePrice { get; set; }

    [Range(0, 50000, ErrorMessage = "Cena pomocníka musí být kladná nebo nulová.")]
    public decimal AdultHelperBasePrice { get; set; }

    [Required(ErrorMessage = "Vyplňte bankovní účet.")]
    public string BankAccount { get; set; } = "";

    [Required(ErrorMessage = "Vyplňte název účtu.")]
    public string BankAccountName { get; set; } = "";

    [Range(1, 5000, ErrorMessage = "Zadejte cílový počet hráčů.")]
    public int TargetPlayerCountTotal { get; set; } = 50;

    public bool IsPublished { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var startsParsed = TryParseRequiredDate(
            StartsAtLocalText,
            "Začátek hry",
            nameof(StartsAtLocalText),
            out var startsAtLocal,
            out var invalidStart);
        if (invalidStart is not null)
        {
            yield return invalidStart;
        }

        var endsParsed = TryParseRequiredDate(
            EndsAtLocalText,
            "Konec hry",
            nameof(EndsAtLocalText),
            out var endsAtLocal,
            out var invalidEnd);
        if (invalidEnd is not null)
        {
            yield return invalidEnd;
        }

        var registrationCloseParsed = TryParseRequiredDate(
            RegistrationClosesAtLocalText,
            "Uzávěrka registrace",
            nameof(RegistrationClosesAtLocalText),
            out var registrationClosesAtLocal,
            out var invalidRegistrationClose);
        if (invalidRegistrationClose is not null)
        {
            yield return invalidRegistrationClose;
        }

        var mealCloseParsed = TryParseRequiredDate(
            MealOrderingClosesAtLocalText,
            "Uzávěrka jídel",
            nameof(MealOrderingClosesAtLocalText),
            out var mealOrderingClosesAtLocal,
            out var invalidMealClose);
        if (invalidMealClose is not null)
        {
            yield return invalidMealClose;
        }

        var paymentDueParsed = TryParseRequiredDate(
            PaymentDueAtLocalText,
            "Termín platby",
            nameof(PaymentDueAtLocalText),
            out _,
            out var invalidPaymentDue);
        if (invalidPaymentDue is not null)
        {
            yield return invalidPaymentDue;
        }

        if (!string.IsNullOrWhiteSpace(AssignmentFreezeAtLocalText)
            && !TryParseOptionalDate(
                AssignmentFreezeAtLocalText,
                "Freeze přidělení",
                nameof(AssignmentFreezeAtLocalText),
                out _,
                out var invalidAssignmentFreeze))
        {
            yield return invalidAssignmentFreeze!;
        }

        if (!startsParsed || !endsParsed || !registrationCloseParsed || !mealCloseParsed || !paymentDueParsed)
        {
            yield break;
        }

        if (endsAtLocal <= startsAtLocal)
        {
            yield return new ValidationResult("Konec hry musí být po začátku.", [nameof(EndsAtLocalText)]);
        }

        if (registrationClosesAtLocal > startsAtLocal)
        {
            yield return new ValidationResult(
                "Uzávěrka registrace musí být nejpozději v okamžiku startu.",
                [nameof(RegistrationClosesAtLocalText)]);
        }

        if (mealOrderingClosesAtLocal > startsAtLocal)
        {
            yield return new ValidationResult(
                "Uzávěrka jídel musí být nejpozději v okamžiku startu.",
                [nameof(MealOrderingClosesAtLocalText)]);
        }
    }

    public CreateGameCommand ToCommand() => new(
        Name,
        Description,
        ParseRequiredDate(StartsAtLocalText),
        ParseRequiredDate(EndsAtLocalText),
        ParseRequiredDate(RegistrationClosesAtLocalText),
        ParseRequiredDate(MealOrderingClosesAtLocalText),
        ParseRequiredDate(PaymentDueAtLocalText),
        ParseOptionalDate(AssignmentFreezeAtLocalText),
        PlayerBasePrice,
        AdultHelperBasePrice,
        BankAccount,
        BankAccountName,
        TargetPlayerCountTotal,
        IsPublished);

    public static CreateGameInput CreateDefaults()
    {
        var start = DateTime.Today.AddMonths(1).AddHours(17);
        return new CreateGameInput
        {
            StartsAtLocalText = FormatDate(start),
            EndsAtLocalText = FormatDate(start.AddDays(2)),
            RegistrationClosesAtLocalText = FormatDate(start.AddDays(-7)),
            MealOrderingClosesAtLocalText = FormatDate(start.AddDays(-10)),
            PaymentDueAtLocalText = FormatDate(start.AddDays(-5)),
            AssignmentFreezeAtLocalText = FormatDate(start.AddDays(-2)),
            PlayerBasePrice = 1200m,
            AdultHelperBasePrice = 0m,
            BankAccount = "CZ6508000000192000145399",
            BankAccountName = "Ovčina z.s.",
            TargetPlayerCountTotal = 80,
            IsPublished = true
        };
    }

    public static string FormatDecimal(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTime value) =>
        value.ToString(DateTimeLocalFormat, CultureInfo.InvariantCulture);

    private static bool TryParseRequiredDate(
        string? value,
        string fieldName,
        string memberName,
        out DateTime parsed,
        out ValidationResult? validationResult)
    {
        if (DateTime.TryParseExact(
            value,
            DateTimeLocalFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out parsed))
        {
            validationResult = null;
            return true;
        }

        validationResult = new ValidationResult($"{fieldName} musí mít platné datum a čas.", [memberName]);
        return false;
    }

    private static bool TryParseOptionalDate(
        string? value,
        string fieldName,
        string memberName,
        out DateTime? parsed,
        out ValidationResult? validationResult)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = null;
            validationResult = null;
            return true;
        }

        if (DateTime.TryParseExact(
            value,
            DateTimeLocalFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var result))
        {
            parsed = result;
            validationResult = null;
            return true;
        }

        parsed = null;
        validationResult = new ValidationResult($"{fieldName} musí mít platné datum a čas.", [memberName]);
        return false;
    }

    private static DateTime ParseRequiredDate(string? value)
    {
        if (DateTime.TryParseExact(
            value,
            DateTimeLocalFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            return parsed;
        }

        throw new ValidationException("Časový údaj formuláře se nepodařilo zpracovat.");
    }

    private static DateTime? ParseOptionalDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParseExact(
            value,
            DateTimeLocalFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            return parsed;
        }

        throw new ValidationException("Časový údaj formuláře se nepodařilo zpracovat.");
    }
}
