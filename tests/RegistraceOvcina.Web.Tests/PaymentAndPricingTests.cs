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

        var registrations = new[]
        {
            new Registration { Role = RegistrationRole.Player, Status = RegistrationStatus.Active },
            new Registration { Role = RegistrationRole.Npc, Status = RegistrationStatus.Active },
            new Registration { Role = RegistrationRole.TechSupport, Status = RegistrationStatus.Active }
        };

        var pricingService = new SubmissionPricingService(TimeProvider.System);

        var total = pricingService.CalculateExpectedTotal(game, registrations);

        Assert.Equal(1500m, total);
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
}
