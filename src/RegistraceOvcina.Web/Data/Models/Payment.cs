namespace RegistraceOvcina.Web.Data;

public sealed class Payment
{
    public int Id { get; set; }
    public int SubmissionId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "CZK";
    public DateTime RecordedAtUtc { get; set; }
    public string? RecordedByUserId { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.BankTransfer;
    public string? Reference { get; set; }
    public string? Note { get; set; }
    public RegistrationSubmission Submission { get; set; } = default!;
}
