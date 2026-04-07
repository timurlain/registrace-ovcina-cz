using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Submissions;

public sealed class SubmissionPricingService(TimeProvider timeProvider)
{
    public decimal CalculateExpectedTotal(Game game, IEnumerable<Registration> registrations, decimal voluntaryDonation = 0m)
    {
        var total = 0m;
        var activeRegs = registrations.Where(x => x.Status == RegistrationStatus.Active).ToList();

        // Group players by family surname for tiered pricing
        var players = activeRegs.Where(x => x.AttendeeType == AttendeeType.Player).ToList();
        var familyGroups = players.GroupBy(x => NormalizeFamilySurname(x.Person.LastName));

        foreach (var family in familyGroups)
        {
            var childIndex = 0;
            foreach (var player in family)
            {
                total += GetChildPrice(game, childIndex);
                childIndex++;
            }
        }

        // Adults
        foreach (var adult in activeRegs.Where(x => x.AttendeeType == AttendeeType.Adult))
        {
            total += game.AdultHelperBasePrice;
        }

        // Food orders
        total += activeRegs.SelectMany(x => x.FoodOrders).Sum(x => x.Price);

        // Lodging
        foreach (var reg in activeRegs)
        {
            total += GetLodgingPrice(game, reg.LodgingPreference);
        }

        // Voluntary donation
        total += Math.Max(0, voluntaryDonation);

        return decimal.Round(total, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Returns the price for a child at the given index within a family group.
    /// Falls back to PlayerBasePrice if tiered prices are not configured (zero).
    /// </summary>
    internal static decimal GetChildPrice(Game game, int childIndex)
    {
        return childIndex switch
        {
            0 => game.PlayerBasePrice,
            1 => game.SecondChildPrice > 0 ? game.SecondChildPrice : game.PlayerBasePrice,
            _ => game.ThirdPlusChildPrice > 0 ? game.ThirdPlusChildPrice : game.PlayerBasePrice
        };
    }

    internal static decimal GetLodgingPrice(Game game, LodgingPreference? preference) => preference switch
    {
        LodgingPreference.Indoor => game.LodgingIndoorPrice,
        LodgingPreference.OwnTent or LodgingPreference.CampOutdoor => game.LodgingOutdoorPrice,
        _ => 0m
    };

    /// <summary>
    /// Normalizes a Czech surname to a family key by stripping common feminine suffixes.
    /// E.g. "Nováková" → "novák", "Branková" → "brank", "Nová" → "nov"
    /// </summary>
    /// <summary>
    /// Normalizes a Czech surname to a family key by stripping common feminine suffixes
    /// and masculine endings to produce a shared root.
    /// E.g. "Novák" and "Nováková" both → "novák"
    ///      "Svoboda" and "Svobodová" both → "svobod"
    ///      "Nový" and "Nová" both → "nov"
    /// </summary>
    internal static string NormalizeFamilySurname(string lastName)
    {
        if (string.IsNullOrWhiteSpace(lastName)) return "";
        var name = lastName.Trim().ToLowerInvariant();

        // Czech feminine → strip -ová suffix (requires at least 2 chars base)
        if (name.EndsWith("ová") && name.Length > 5)
        {
            return name[..^3];
        }

        // Feminine adjective form: strip -á
        if (name.EndsWith("á") && name.Length > 2)
        {
            return name[..^1];
        }

        // Masculine adjective form: strip -ý to match feminine -á → same root
        if (name.EndsWith("ý") && name.Length > 2)
        {
            return name[..^1];
        }

        // Masculine noun ending in -a (e.g. Svoboda): strip to match Svobodová → "svobod"
        if (name.EndsWith("a") && name.Length > 3)
        {
            return name[..^1];
        }

        return name;
    }

    public PricingResult CalculateBreakdown(Game game, IEnumerable<Registration> registrations, decimal voluntaryDonation = 0m)
    {
        var lines = new List<PriceBreakdownLine>();
        var activeRegs = registrations.Where(x => x.Status == RegistrationStatus.Active).ToList();

        var players = activeRegs.Where(x => x.AttendeeType == AttendeeType.Player).ToList();
        var familyGroups = players.GroupBy(x => NormalizeFamilySurname(x.Person.LastName));

        var firstChildCount = 0;
        var secondChildCount = 0;
        var thirdPlusChildCount = 0;

        foreach (var family in familyGroups)
        {
            var childIndex = 0;
            foreach (var _ in family)
            {
                switch (childIndex)
                {
                    case 0: firstChildCount++; break;
                    case 1: secondChildCount++; break;
                    default: thirdPlusChildCount++; break;
                }
                childIndex++;
            }
        }

        if (firstChildCount > 0 && game.PlayerBasePrice > 0)
            lines.Add(new("Hráči — 1. dítě v rodině", firstChildCount, game.PlayerBasePrice, firstChildCount * game.PlayerBasePrice));
        if (secondChildCount > 0)
        {
            var price = GetChildPrice(game, 1);
            if (price > 0) lines.Add(new("Hráči — 2. dítě v rodině", secondChildCount, price, secondChildCount * price));
        }
        if (thirdPlusChildCount > 0)
        {
            var price = GetChildPrice(game, 2);
            if (price > 0) lines.Add(new("Hráči — 3.+ dítě v rodině", thirdPlusChildCount, price, thirdPlusChildCount * price));
        }

        var adultCount = activeRegs.Count(x => x.AttendeeType == AttendeeType.Adult);
        if (adultCount > 0 && game.AdultHelperBasePrice > 0)
            lines.Add(new("Dospělí / NPC", adultCount, game.AdultHelperBasePrice, adultCount * game.AdultHelperBasePrice));

        var foodOrders = activeRegs.SelectMany(x => x.FoodOrders).ToList();
        var foodTotal = foodOrders.Sum(x => x.Price);
        if (foodTotal > 0)
            lines.Add(new("Stravování", foodOrders.Count, 0, foodTotal));

        // Lodging
        var indoorCount = activeRegs.Count(x => x.LodgingPreference == LodgingPreference.Indoor);
        var outdoorCount = activeRegs.Count(x => x.LodgingPreference is LodgingPreference.OwnTent or LodgingPreference.CampOutdoor);

        if (indoorCount > 0 && game.LodgingIndoorPrice > 0)
        {
            lines.Add(new PriceBreakdownLine("Ubytování uvnitř", indoorCount, game.LodgingIndoorPrice, indoorCount * game.LodgingIndoorPrice));
        }
        if (outdoorCount > 0 && game.LodgingOutdoorPrice > 0)
        {
            lines.Add(new PriceBreakdownLine("Ubytování venku/stan", outdoorCount, game.LodgingOutdoorPrice, outdoorCount * game.LodgingOutdoorPrice));
        }

        if (voluntaryDonation > 0)
            lines.Add(new("Dobrovolný příspěvek", 1, voluntaryDonation, voluntaryDonation));

        var total = decimal.Round(lines.Sum(x => x.Total), 2, MidpointRounding.AwayFromZero);
        return new PricingResult(lines, total);
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

public sealed record PricingResult(IReadOnlyList<PriceBreakdownLine> Lines, decimal Total);
