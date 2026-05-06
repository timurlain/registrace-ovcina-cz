namespace RegistraceOvcina.Web.Features.Feedback;

public sealed record FeedbackQuestion
{
    public string Key { get; init; }
    public string Label { get; init; }
    public string? HelpText { get; init; }
    public IReadOnlyList<string> Placeholders { get; init; }

    public FeedbackQuestion(
        string key,
        string label,
        string? helpText = null,
        IReadOnlyList<string>? placeholders = null)
    {
        Key = key;
        Label = label;
        HelpText = string.IsNullOrWhiteSpace(helpText) ? null : helpText;
        Placeholders = placeholders ?? Array.Empty<string>();
    }
}
