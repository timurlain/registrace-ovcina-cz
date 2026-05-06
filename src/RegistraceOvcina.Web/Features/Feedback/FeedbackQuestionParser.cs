using System.Text.Json;
using System.Text.Json.Serialization;

namespace RegistraceOvcina.Web.Features.Feedback;

public static class FeedbackQuestionParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static FeedbackQuestionSet Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return FeedbackQuestionSet.Empty;
        }

        List<RawQuestion>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<List<RawQuestion>>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new FormatException(
                $"Invalid FeedbackQuestionSet JSON: {ex.Message}", ex);
        }

        if (raw is null)
        {
            return FeedbackQuestionSet.Empty;
        }

        var questions = raw.Select(r => new FeedbackQuestion(
            r.Key ?? string.Empty,
            r.Label ?? string.Empty,
            r.HelpText,
            r.Placeholders ?? Array.Empty<string>(),
            r.Rows));

        return new FeedbackQuestionSet(questions);
    }

    public static string Serialize(FeedbackQuestionSet set)
    {
        var raw = set.Questions.Select(q => new RawQuestion
        {
            Key = q.Key,
            Label = q.Label,
            HelpText = q.HelpText,
            Placeholders = q.Placeholders?.ToArray(),
            Rows = q.Rows,
        }).ToList();

        return JsonSerializer.Serialize(raw, Options);
    }

    private sealed class RawQuestion
    {
        [JsonPropertyName("key")] public string? Key { get; set; }
        [JsonPropertyName("label")] public string? Label { get; set; }
        [JsonPropertyName("helpText")] public string? HelpText { get; set; }
        [JsonPropertyName("placeholders")] public string[]? Placeholders { get; set; }
        [JsonPropertyName("rows")] public int? Rows { get; set; }
    }
}
