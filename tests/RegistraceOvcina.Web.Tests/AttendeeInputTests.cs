using System.ComponentModel.DataAnnotations;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Submissions;

namespace RegistraceOvcina.Web.Tests;

public sealed class AttendeeInputTests
{
    private static List<ValidationResult> ValidateInput(AttendeeInput input)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(input);
        Validator.TryValidateObject(input, context, results, validateAllProperties: true);
        // IValidatableObject.Validate is called automatically by TryValidateObject
        return results;
    }

    private static AttendeeInput CreateValidPlayerInput() => new()
    {
        FirstName = "Jan",
        LastName = "Novák",
        BirthYear = 2000,
        AttendeeType = AttendeeType.Player,
        PlayerSubType = PlayerSubType.Pvp
    };

    private static AttendeeInput CreateValidAdultInput() => new()
    {
        FirstName = "Jana",
        LastName = "Nováková",
        BirthYear = 1985,
        AttendeeType = AttendeeType.Adult,
        AdultRole_PlayMonster = true
    };

    [Fact]
    public void Validation_PlayerWithoutSubType_Fails()
    {
        var input = CreateValidPlayerInput();
        input.PlayerSubType = null;

        var results = ValidateInput(input);

        Assert.Contains(results, r => r.ErrorMessage == "Vyberte kategorii hráče.");
    }

    [Fact]
    public void Validation_PlayerWithSubType_Passes()
    {
        var input = CreateValidPlayerInput();

        var results = ValidateInput(input);

        Assert.DoesNotContain(results, r => r.ErrorMessage == "Vyberte kategorii hráče.");
    }

    [Fact]
    public void Validation_AdultWithoutRoles_Fails()
    {
        var input = new AttendeeInput
        {
            FirstName = "Jana",
            LastName = "Nováková",
            BirthYear = 1985,
            AttendeeType = AttendeeType.Adult,
            // All AdultRole_ bools default to false
        };

        var results = ValidateInput(input);

        Assert.Contains(results, r => r.ErrorMessage == "Vyberte alespoň jednu roli dospělého.");
    }

    [Fact]
    public void Validation_AdultWithRoles_Passes()
    {
        var input = CreateValidAdultInput();

        var results = ValidateInput(input);

        Assert.DoesNotContain(results, r => r.ErrorMessage == "Vyberte alespoň jednu roli dospělého.");
    }

    [Fact]
    public void ComputedAdultRoles_CombinesFlagsCorrectly()
    {
        var input = new AttendeeInput
        {
            AdultRole_PlayMonster = true,
            AdultRole_TechSupport = true
        };

        Assert.Equal(
            AdultRoleFlags.PlayMonster | AdultRoleFlags.TechSupport,
            input.ComputedAdultRoles);
    }

    [Fact]
    public void ComputedAdultRoles_NoneWhenAllFalse()
    {
        var input = new AttendeeInput();

        Assert.Equal(AdultRoleFlags.None, input.ComputedAdultRoles);
    }

    [Fact]
    public void Validation_MinorWithoutGuardian_FailsWithGuardianErrors()
    {
        // Use a birth year that makes the person < 18 relative to DateTime.UtcNow.Year
        var minorBirthYear = DateTime.UtcNow.Year - 10;

        var input = new AttendeeInput
        {
            FirstName = "Tomáš",
            LastName = "Malý",
            BirthYear = minorBirthYear,
            AttendeeType = AttendeeType.Player,
            PlayerSubType = PlayerSubType.Pvp,
            // No guardian info
        };

        var results = ValidateInput(input);

        Assert.Contains(results, r => r.ErrorMessage == "U nezletilého je povinné jméno zákonného zástupce.");
        Assert.Contains(results, r => r.ErrorMessage == "U nezletilého je povinný vztah zákonného zástupce.");
        Assert.Contains(results, r => r.ErrorMessage == "Potvrďte souhlas zákonného zástupce.");
    }
}
