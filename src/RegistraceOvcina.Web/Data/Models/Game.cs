namespace RegistraceOvcina.Web.Data;

public sealed class Game
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public DateTime RegistrationClosesAtUtc { get; set; }
    public DateTime MealOrderingClosesAtUtc { get; set; }
    public DateTime PaymentDueAtUtc { get; set; }
    public DateTime? AssignmentFreezeAtUtc { get; set; }
    public decimal PlayerBasePrice { get; set; }
    public decimal SecondChildPrice { get; set; }
    public decimal ThirdPlusChildPrice { get; set; }
    public decimal AdultHelperBasePrice { get; set; }
    public decimal LodgingIndoorPrice { get; set; }
    public decimal LodgingOutdoorPrice { get; set; }
    public string BankAccount { get; set; } = "";
    public string BankAccountName { get; set; } = "";
    public VariableSymbolStrategy VariableSymbolStrategy { get; set; }
    public int TargetPlayerCountTotal { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<GameKingdomTarget> KingdomTargets { get; set; } = [];
    public List<MealOption> MealOptions { get; set; } = [];
    public List<RegistrationSubmission> Submissions { get; set; } = [];
}
