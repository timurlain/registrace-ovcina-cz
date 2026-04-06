using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Payments;
using RegistraceOvcina.Web.Features.Submissions;

namespace RegistraceOvcina.Web.Tests;

public sealed class PaymentAndPricingTests
{
    [Fact]
    public void CalculateExpectedTotal_UsesRoleBasedPricing()
    {
        var game = new Game
        {
            PlayerBasePrice = 1500m,
            AdultHelperBasePrice = 0m
        };

        var person = new Person { FirstName = "Jan", LastName = "Novák" };
        var registrations = new[]
        {
            new Registration { AttendeeType = AttendeeType.Player, Status = RegistrationStatus.Active, Person = person },
            new Registration { AttendeeType = AttendeeType.Adult, Status = RegistrationStatus.Active, Person = person },
            new Registration { AttendeeType = AttendeeType.Adult, Status = RegistrationStatus.Active, Person = person }
        };

        var pricingService = new SubmissionPricingService(TimeProvider.System);

        var total = pricingService.CalculateExpectedTotal(game, registrations);

        Assert.Equal(1500m, total);
    }

    [Fact]
    public void CalculateExpectedTotal_TieredFamilyPricing_SameFamily()
    {
        var game = new Game
        {
            PlayerBasePrice = 200m,
            SecondChildPrice = 150m,
            ThirdPlusChildPrice = 100m,
            AdultHelperBasePrice = 0m
        };

        var registrations = new[]
        {
            new Registration { AttendeeType = AttendeeType.Player, Status = RegistrationStatus.Active, Person = new Person { FirstName = "Jan", LastName = "Novák" } },
            new Registration { AttendeeType = AttendeeType.Player, Status = RegistrationStatus.Active, Person = new Person { FirstName = "Jana", LastName = "Nováková" } },
            new Registration { AttendeeType = AttendeeType.Player, Status = RegistrationStatus.Active, Person = new Person { FirstName = "Petr", LastName = "Novák" } },
        };

        var pricingService = new SubmissionPricingService(TimeProvider.System);

        // All Novák/Nováková = one family: 200 + 150 + 100
        var total = pricingService.CalculateExpectedTotal(game, registrations);

        Assert.Equal(450m, total);
    }

    [Fact]
    public void CalculateExpectedTotal_TieredFamilyPricing_DifferentFamilies()
    {
        var game = new Game
        {
            PlayerBasePrice = 200m,
            SecondChildPrice = 150m,
            ThirdPlusChildPrice = 100m,
            AdultHelperBasePrice = 0m
        };

        var registrations = new[]
        {
            new Registration { AttendeeType = AttendeeType.Player, Status = RegistrationStatus.Active, Person = new Person { FirstName = "Jan", LastName = "Novák" } },
            new Registration { AttendeeType = AttendeeType.Player, Status = RegistrationStatus.Active, Person = new Person { FirstName = "Jana", LastName = "Nováková" } },
            new Registration { AttendeeType = AttendeeType.Player, Status = RegistrationStatus.Active, Person = new Person { FirstName = "Karel", LastName = "Svoboda" } },
            new Registration { AttendeeType = AttendeeType.Player, Status = RegistrationStatus.Active, Person = new Person { FirstName = "Marie", LastName = "Svobodová" } },
        };

        var pricingService = new SubmissionPricingService(TimeProvider.System);

        // Novák family: 200 + 150 = 350
        // Svoboda family: 200 + 150 = 350
        var total = pricingService.CalculateExpectedTotal(game, registrations);

        Assert.Equal(700m, total);
    }

    [Fact]
    public void CalculateExpectedTotal_FallsBackToPlayerBasePrice_WhenTiersNotConfigured()
    {
        var game = new Game
        {
            PlayerBasePrice = 1500m,
            SecondChildPrice = 0m,
            ThirdPlusChildPrice = 0m,
            AdultHelperBasePrice = 0m
        };

        var registrations = new[]
        {
            new Registration { AttendeeType = AttendeeType.Player, Status = RegistrationStatus.Active, Person = new Person { FirstName = "Jan", LastName = "Novák" } },
            new Registration { AttendeeType = AttendeeType.Player, Status = RegistrationStatus.Active, Person = new Person { FirstName = "Jana", LastName = "Nováková" } },
        };

        var pricingService = new SubmissionPricingService(TimeProvider.System);

        // Both at full price when tiers are 0
        var total = pricingService.CalculateExpectedTotal(game, registrations);

        Assert.Equal(3000m, total);
    }

    [Fact]
    public void CalculateExpectedTotal_IncludesVoluntaryDonation()
    {
        var game = new Game
        {
            PlayerBasePrice = 200m,
            SecondChildPrice = 150m,
            ThirdPlusChildPrice = 100m,
            AdultHelperBasePrice = 0m
        };

        var registrations = new[]
        {
            new Registration { AttendeeType = AttendeeType.Player, Status = RegistrationStatus.Active, Person = new Person { FirstName = "Jan", LastName = "Novák" } },
        };

        var pricingService = new SubmissionPricingService(TimeProvider.System);

        var total = pricingService.CalculateExpectedTotal(game, registrations, voluntaryDonation: 500m);

        Assert.Equal(700m, total);
    }

    [Fact]
    public void NormalizeFamilySurname_HandlesCzechFeminineForms()
    {
        // Novák / Nováková → same key
        Assert.Equal("novák", SubmissionPricingService.NormalizeFamilySurname("Nováková"));
        Assert.Equal("novák", SubmissionPricingService.NormalizeFamilySurname("Novák"));

        // Svoboda / Svobodová → same key
        Assert.Equal("svobod", SubmissionPricingService.NormalizeFamilySurname("Svobodová"));
        Assert.Equal("svobod", SubmissionPricingService.NormalizeFamilySurname("Svoboda"));

        // Nový / Nová → same key
        Assert.Equal("nov", SubmissionPricingService.NormalizeFamilySurname("Nová"));
        Assert.Equal("nov", SubmissionPricingService.NormalizeFamilySurname("Nový"));

        // Edge cases
        Assert.Equal("", SubmissionPricingService.NormalizeFamilySurname(""));
        Assert.Equal("", SubmissionPricingService.NormalizeFamilySurname("  "));
    }

    [Fact]
    public void Build_CreatesSpaydPayloadAndVariableSymbol()
    {
        var service = new SpaydPaymentQrService();
        var game = new Game
        {
            Name = "Ovčina 2026",
            BankAccount = "CZ6508000000192000145399",
            BankAccountName = "Ovčina z.s."
        };

        var submission = new RegistrationSubmission
        {
            Id = 42,
            ExpectedTotalAmount = 1500m
        };

        var result = service.Build(game, submission);

        Assert.NotNull(result);
        Assert.Contains("ACC:CZ6508000000192000145399", result!.SpaydPayload);
        Assert.Contains("AM:1500.00", result.SpaydPayload);
        Assert.Contains("X-VS:0000000042", result.SpaydPayload);
        Assert.Contains("<svg", result.SvgMarkup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_SvgContainsViewBox()
    {
        var service = new SpaydPaymentQrService();
        var game = new Game
        {
            Name = "Ovčina 2026",
            BankAccount = "CZ6508000000192000145399",
            BankAccountName = "Ovčina z.s."
        };

        var submission = new RegistrationSubmission
        {
            Id = 1,
            ExpectedTotalAmount = 500m
        };

        var result = service.Build(game, submission);

        Assert.NotNull(result);
        Assert.Contains("viewBox", result!.SvgMarkup, StringComparison.OrdinalIgnoreCase);
    }
}
