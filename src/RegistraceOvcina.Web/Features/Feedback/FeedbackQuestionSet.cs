namespace RegistraceOvcina.Web.Features.Feedback;

public sealed class FeedbackQuestionSet
{
    public IReadOnlyList<FeedbackQuestion> Questions { get; }

    public FeedbackQuestionSet(IEnumerable<FeedbackQuestion> questions)
    {
        var list = (questions ?? Array.Empty<FeedbackQuestion>()).ToList();

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var q in list)
        {
            if (string.IsNullOrWhiteSpace(q.Key))
            {
                throw new FormatException(
                    "FeedbackQuestionSet question is missing a non-blank 'key'.");
            }

            if (string.IsNullOrWhiteSpace(q.Label))
            {
                throw new FormatException(
                    $"FeedbackQuestionSet question '{q.Key}' is missing a non-blank 'label'.");
            }

            if (!seenKeys.Add(q.Key))
            {
                throw new FormatException(
                    $"FeedbackQuestionSet has duplicate question key '{q.Key}'.");
            }
        }

        Questions = list;
    }

    public static FeedbackQuestionSet Empty { get; } = new(Array.Empty<FeedbackQuestion>());
}
