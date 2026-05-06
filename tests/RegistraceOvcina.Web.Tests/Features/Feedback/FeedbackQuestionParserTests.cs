using RegistraceOvcina.Web.Features.Feedback;

namespace RegistraceOvcina.Web.Tests.Features.Feedback;

public class FeedbackQuestionParserTests
{
    [Fact]
    public void Parse_returns_empty_when_json_is_null_or_blank()
    {
        Assert.Empty(FeedbackQuestionParser.Parse(null).Questions);
        Assert.Empty(FeedbackQuestionParser.Parse("").Questions);
        Assert.Empty(FeedbackQuestionParser.Parse("   ").Questions);
    }

    [Fact]
    public void Parse_round_trips_well_formed_json()
    {
        const string json = """
        [
          {
            "key": "strongest_moment",
            "label": "Jaký jsi měl zážitek?",
            "helpText": "Krátké věty stačí.",
            "placeholders": ["a", "b", "c"]
          }
        ]
        """;

        var set = FeedbackQuestionParser.Parse(json);

        var q = Assert.Single(set.Questions);
        Assert.Equal("strongest_moment", q.Key);
        Assert.Equal("Jaký jsi měl zážitek?", q.Label);
        Assert.Equal("Krátké věty stačí.", q.HelpText);
        Assert.Equal(new[] { "a", "b", "c" }, q.Placeholders);
    }

    [Fact]
    public void Parse_throws_with_clear_message_on_invalid_json()
    {
        var ex = Assert.Throws<FormatException>(() => FeedbackQuestionParser.Parse("{not json}"));
        Assert.Contains("FeedbackQuestionSet", ex.Message);
    }

    [Fact]
    public void Parse_rejects_questions_with_blank_key()
    {
        const string json = """[{ "key": "", "label": "x" }]""";
        Assert.Throws<FormatException>(() => FeedbackQuestionParser.Parse(json));
    }

    [Fact]
    public void Parse_rejects_duplicate_keys()
    {
        const string json = """
        [
          { "key": "k", "label": "a" },
          { "key": "k", "label": "b" }
        ]
        """;
        Assert.Throws<FormatException>(() => FeedbackQuestionParser.Parse(json));
    }

    [Fact]
    public void Parse_rejects_questions_with_missing_label()
    {
        const string json = """[{ "key": "k" }]""";
        var ex = Assert.Throws<FormatException>(() => FeedbackQuestionParser.Parse(json));
        Assert.Contains("label", ex.Message);
        Assert.Contains("k", ex.Message);
    }

    [Fact]
    public void Parse_rejects_questions_with_blank_label()
    {
        const string json = """[{ "key": "k", "label": "   " }]""";
        Assert.Throws<FormatException>(() => FeedbackQuestionParser.Parse(json));
    }

    [Fact]
    public void Parse_accepts_helpText_absent()
    {
        const string json = """[{ "key": "k", "label": "L" }]""";
        var set = FeedbackQuestionParser.Parse(json);
        Assert.Null(Assert.Single(set.Questions).HelpText);
    }

    [Fact]
    public void Parse_rejects_pascal_case_property_names()
    {
        // Locks in that we explicitly require lower-case JSON keys.
        // If someone later flips PropertyNameCaseInsensitive on, this catches it.
        const string json = """[{ "Key": "k", "Label": "L" }]""";
        Assert.Throws<FormatException>(() => FeedbackQuestionParser.Parse(json));
    }

    [Fact]
    public void Serialize_emits_camelCase_property_names()
    {
        var set = new FeedbackQuestionSet(new[]
        {
            new FeedbackQuestion("k", "L", "H", new[] { "a" }),
        });

        var json = FeedbackQuestionParser.Serialize(set);

        Assert.Contains("\"key\":", json);
        Assert.Contains("\"label\":", json);
        Assert.Contains("\"helpText\":", json);
        Assert.Contains("\"placeholders\":", json);
        Assert.DoesNotContain("\"Key\":", json);
    }

    [Fact]
    public void FeedbackQuestionSet_constructor_rejects_blank_key()
    {
        Assert.Throws<FormatException>(() => new FeedbackQuestionSet(new[]
        {
            new FeedbackQuestion("", "L"),
        }));
    }

    [Fact]
    public void FeedbackQuestionSet_constructor_rejects_duplicate_keys()
    {
        Assert.Throws<FormatException>(() => new FeedbackQuestionSet(new[]
        {
            new FeedbackQuestion("k", "L"),
            new FeedbackQuestion("k", "L2"),
        }));
    }

    [Fact]
    public void FeedbackQuestion_normalizes_blank_helpText_to_null()
    {
        var q = new FeedbackQuestion("k", "L", "  ");
        Assert.Null(q.HelpText);
    }

    [Fact]
    public void FeedbackQuestion_defaults_placeholders_to_empty_when_null()
    {
        var q = new FeedbackQuestion("k", "L");
        Assert.NotNull(q.Placeholders);
        Assert.Empty(q.Placeholders);
    }

    [Fact]
    public void Serialize_round_trips_back_to_parseable_json()
    {
        var set = new FeedbackQuestionSet(new[]
        {
            new FeedbackQuestion("k1", "L1", "H1", new[] { "p" }),
            new FeedbackQuestion("k2", "L2", null, Array.Empty<string>()),
        });

        var json = FeedbackQuestionParser.Serialize(set);
        var roundTripped = FeedbackQuestionParser.Parse(json);

        Assert.Equal(2, roundTripped.Questions.Count);

        Assert.Equal("k1", roundTripped.Questions[0].Key);
        Assert.Equal("L1", roundTripped.Questions[0].Label);
        Assert.Equal("H1", roundTripped.Questions[0].HelpText);
        Assert.Equal(new[] { "p" }, roundTripped.Questions[0].Placeholders);

        Assert.Equal("k2", roundTripped.Questions[1].Key);
        Assert.Equal("L2", roundTripped.Questions[1].Label);
        Assert.Null(roundTripped.Questions[1].HelpText);
        Assert.Empty(roundTripped.Questions[1].Placeholders);
    }
}
