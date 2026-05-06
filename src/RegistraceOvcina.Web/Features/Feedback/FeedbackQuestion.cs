namespace RegistraceOvcina.Web.Features.Feedback;

public sealed record FeedbackQuestion
{
    public string Key { get; init; }
    public string Label { get; init; }
    public string? HelpText { get; init; }
    public IReadOnlyList<string> Placeholders { get; init; }

    /// <summary>
    /// Optional override for the textarea row count. <c>null</c> means "use the
    /// form's default" (currently 4). Question authors can opt a single question
    /// into a taller textarea — e.g. <c>character_story</c> with <c>"rows": 12</c>
    /// — without affecting the rest of the form.
    /// </summary>
    public int? Rows { get; init; }

    public FeedbackQuestion(
        string key,
        string label,
        string? helpText = null,
        IReadOnlyList<string>? placeholders = null,
        int? rows = null)
    {
        Key = key;
        Label = label;
        HelpText = string.IsNullOrWhiteSpace(helpText) ? null : helpText;
        Placeholders = placeholders ?? Array.Empty<string>();
        Rows = rows is > 0 ? rows : null;
    }
}
