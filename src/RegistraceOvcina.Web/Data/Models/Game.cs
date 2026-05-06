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

    /// <summary>
    /// Free-form JSON blob with organizational info (location, gathering point, what to bring,
    /// schedule outline, organizer contact). Parsed by API endpoints into structured response.
    /// </summary>
    public string? OrganizationInfo { get; set; }
    public string? FeedbackKidQuestionsJson { get; set; }
    public string? FeedbackAdultQuestionsJson { get; set; }
    public DateTimeOffset? FeedbackOpensAtUtc { get; set; }
    public DateTimeOffset? FeedbackClosesAtUtc { get; set; }

    /// <summary>
    /// Optional organizer override for the household-bundle email subject. Tokens
    /// (e.g. <c>{ReminderPrefix}</c>, <c>{GameName}</c>) are substituted at send
    /// time. NULL → fall back to the canonical default in
    /// <see cref="Features.Feedback.FeedbackEmailRenderer"/>.
    /// </summary>
    public string? FeedbackBundleSubjectTemplate { get; set; }

    /// <summary>
    /// Optional organizer override for the household-bundle email HTML body.
    /// Supports <c>{ContactName}</c>, <c>{GameName}</c>, <c>{Deadline}</c>,
    /// <c>{ReminderPrefix}</c>, <c>{ReminderIntro}</c>, and <c>{Entries}</c>.
    /// NULL → fall back to the hardcoded default body.
    /// </summary>
    public string? FeedbackBundleHtmlTemplate { get; set; }

    /// <summary>
    /// Optional organizer override for the adult-individual email subject.
    /// Tokens are substituted at send time. NULL → fall back to default.
    /// </summary>
    public string? FeedbackAdultIndividualSubjectTemplate { get; set; }

    /// <summary>
    /// Optional organizer override for the adult-individual email HTML body.
    /// Supports <c>{AttendeeName}</c>, <c>{GameName}</c>, <c>{Deadline}</c>,
    /// <c>{ReminderPrefix}</c>, <c>{ReminderIntro}</c>, <c>{TokenLink}</c>,
    /// and <c>{ButtonHtml}</c>. NULL → fall back to the hardcoded default.
    /// </summary>
    public string? FeedbackAdultIndividualHtmlTemplate { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<GameKingdomTarget> KingdomTargets { get; set; } = [];
    public List<MealOption> MealOptions { get; set; } = [];
    public List<RegistrationSubmission> Submissions { get; set; } = [];
}
