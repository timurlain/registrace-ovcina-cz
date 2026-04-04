using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Submissions;

public sealed class SubmissionPricingService(TimeProvider timeProvider)
{
    public decimal CalculateExpectedTotal(Game game, IEnumerable<Registration> registrations)
    {
        var total = 0m;

        foreach (var registration in registrations.Where(x => x.Status == RegistrationStatus.Active))
        {
            total += registration.Role == RegistrationRole.Player
                ? game.PlayerBasePrice
                : game.AdultHelperBasePrice;
        }

        return decimal.Round(total, 2, MidpointRounding.AwayFromZero);
    }

    public BalanceStatus CalculateBalanceStatus(decimal expectedAmount, decimal paidAmount)
    {
        if (expectedAmount <= 0)
        {
            return BalanceStatus.Balanced;
        }

        if (paidAmount <= 0)
        {
            return BalanceStatus.Unpaid;
        }

        if (paidAmount < expectedAmount)
        {
            return BalanceStatus.Underpaid;
        }

        if (paidAmount > expectedAmount)
        {
            return BalanceStatus.Overpaid;
        }

        return BalanceStatus.Balanced;
    }

    public bool RequiresGuardianData(int birthYear) => timeProvider.GetUtcNow().Year - birthYear < 18;
}
