using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Feedback;

namespace RegistraceOvcina.Web.Tests.Features.Feedback;

/// <summary>
/// Covers <see cref="FeedbackService"/> — load/save/submit/status for one
/// registration's feedback. Mirrors the in-memory + private TestDbContextFactory
/// pattern used by <c>FeedbackTokenServiceTests</c> and
/// <c>FeedbackOptionsServiceTests</c>; "now" is injected via a tiny
/// <see cref="FixedTimeProvider"/> double because
/// <c>Microsoft.Extensions.TimeProvider.Testing</c> is not a project dependency.
/// </summary>
public sealed class FeedbackServiceTests
{
    // The fixed game window used by the seed helper:
    //   StartsAtUtc = FixedUtc + 30 d
    //   EndsAtUtc   = FixedUtc + 32 d
    // Default (no Game.FeedbackOpensAtUtc / FeedbackClosesAtUtc) means the
    // window is [EndsAtUtc, EndsAtUtc + 30 d].
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    private const string KidJson = """[{"key":"co_libilo","label":"Co se ti líbilo?"},{"key":"co_zlepsit","label":"Co bys zlepšil?"}]""";
    private const string AdultJson = """[{"key":"organizace","label":"Organizace?"},{"key":"navrh","label":"Návrh?"}]""";

    // -------------------------------------------------------------------------
    // GetViewAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetViewAsync_returns_null_for_unknown_registration()
    {
        var options = CreateOptions();
        var sut = CreateSut(options, NowDuringWindow());

        var view = await sut.GetViewAsync(999, CancellationToken.None);

        Assert.Null(view);
    }

    [Fact]
    public async Task GetViewAsync_returns_view_for_player_with_kid_questions()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        var sut = CreateSut(options, NowDuringWindow());

        var view = await sut.GetViewAsync(registrationId, CancellationToken.None);

        Assert.NotNull(view);
        Assert.Equal(AttendeeType.Player, view!.AttendeeType);
        Assert.Equal(2, view.Questions.Questions.Count);
        Assert.Equal("co_libilo", view.Questions.Questions[0].Key);
        Assert.Equal("Kid One", view.AttendeeFullName);
    }

    [Fact]
    public async Task GetViewAsync_returns_view_for_adult_with_adult_questions()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Adult);
        var sut = CreateSut(options, NowDuringWindow());

        var view = await sut.GetViewAsync(registrationId, CancellationToken.None);

        Assert.NotNull(view);
        Assert.Equal(AttendeeType.Adult, view!.AttendeeType);
        Assert.Equal(2, view.Questions.Questions.Count);
        Assert.Equal("organizace", view.Questions.Questions[0].Key);
    }

    [Fact]
    public async Task GetViewAsync_returns_empty_answers_when_no_response_exists()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        var sut = CreateSut(options, NowDuringWindow());

        var view = await sut.GetViewAsync(registrationId, CancellationToken.None);

        Assert.NotNull(view);
        Assert.Empty(view!.Answers);
        Assert.Null(view.UpdatedAtUtc);
        Assert.Null(view.SubmittedAtUtc);
    }

    [Fact]
    public async Task GetViewAsync_returns_existing_answers_when_response_exists()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        await SeedExistingResponseAsync(options, registrationId, """{"co_libilo":"Bitva u brodu"}""");
        var sut = CreateSut(options, NowDuringWindow());

        var view = await sut.GetViewAsync(registrationId, CancellationToken.None);

        Assert.NotNull(view);
        Assert.Single(view!.Answers);
        Assert.Equal("Bitva u brodu", view.Answers["co_libilo"]);
        Assert.NotNull(view.UpdatedAtUtc);
    }

    [Fact]
    public async Task GetViewAsync_marks_readonly_before_window_opens()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        // Window is [EndsAtUtc=FixedUtc+32d, +30d]. "Now" before EndsAtUtc.
        var sut = CreateSut(options, new DateTimeOffset(FixedUtc.AddDays(31), TimeSpan.Zero));

        var view = await sut.GetViewAsync(registrationId, CancellationToken.None);

        Assert.NotNull(view);
        Assert.True(view!.IsReadOnly);
        Assert.Equal("NotOpen", view.ReadOnlyReason);
    }

    [Fact]
    public async Task GetViewAsync_marks_readonly_after_window_closes()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        // Default close = EndsAtUtc + 30 d = FixedUtc + 62 d. "Now" after.
        var sut = CreateSut(options, new DateTimeOffset(FixedUtc.AddDays(70), TimeSpan.Zero));

        var view = await sut.GetViewAsync(registrationId, CancellationToken.None);

        Assert.NotNull(view);
        Assert.True(view!.IsReadOnly);
        Assert.Equal("Closed", view.ReadOnlyReason);
    }

    [Fact]
    public async Task GetViewAsync_uses_default_window_when_game_overrides_null()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        var sut = CreateSut(options, NowDuringWindow());

        var view = await sut.GetViewAsync(registrationId, CancellationToken.None);

        Assert.NotNull(view);
        // Opens at EndsAtUtc (= FixedUtc + 32 d). Closes 30 days later.
        var expectedOpens = new DateTimeOffset(FixedUtc.AddDays(32), TimeSpan.Zero);
        var expectedCloses = expectedOpens.AddDays(30);
        Assert.Equal(expectedOpens, view!.EffectiveOpensAt);
        Assert.Equal(expectedCloses, view.EffectiveClosesAt);
    }

    // -------------------------------------------------------------------------
    // SaveAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaveAsync_creates_new_response_on_first_save()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        var sut = CreateSut(options, NowDuringWindow());

        var result = await sut.SaveAsync(
            registrationId,
            new Dictionary<string, string> { ["co_libilo"] = "draci" },
            FeedbackEditedBy.Self,
            editedByPersonId: null,
            CancellationToken.None);

        Assert.True(result.Persisted);
        Assert.Null(result.RejectionReason);

        await using var verify = new ApplicationDbContext(options);
        var response = await verify.FeedbackResponses.SingleAsync(x => x.RegistrationId == registrationId);
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(response.AnswersJson)!;
        Assert.Equal("draci", dict["co_libilo"]);
    }

    [Fact]
    public async Task SaveAsync_updates_existing_response_on_subsequent_save()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        var sut = CreateSut(options, NowDuringWindow());

        await sut.SaveAsync(
            registrationId,
            new Dictionary<string, string> { ["co_libilo"] = "first" },
            FeedbackEditedBy.Self,
            null,
            CancellationToken.None);

        await sut.SaveAsync(
            registrationId,
            new Dictionary<string, string> { ["co_libilo"] = "second" },
            FeedbackEditedBy.Self,
            null,
            CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var responses = await verify.FeedbackResponses
            .Where(x => x.RegistrationId == registrationId).ToListAsync();
        var single = Assert.Single(responses);
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(single.AnswersJson)!;
        Assert.Equal("second", dict["co_libilo"]);
    }

    [Fact]
    public async Task SaveAsync_records_audit_fields()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        var now = NowDuringWindow();
        var sut = CreateSut(options, now);

        await sut.SaveAsync(
            registrationId,
            new Dictionary<string, string> { ["co_libilo"] = "x" },
            FeedbackEditedBy.HouseholdContact,
            editedByPersonId: 1,
            CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var response = await verify.FeedbackResponses.SingleAsync(x => x.RegistrationId == registrationId);
        Assert.Equal(FeedbackEditedBy.HouseholdContact, response.LastEditedBy);
        Assert.Equal(1, response.LastEditedByPersonId);
        Assert.Equal(now, response.UpdatedAtUtc);
        Assert.Equal(now, response.CreatedAtUtc);
    }

    [Fact]
    public async Task SaveAsync_returns_NotOpen_when_window_not_yet_open()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        // Before EndsAtUtc.
        var sut = CreateSut(options, new DateTimeOffset(FixedUtc.AddDays(10), TimeSpan.Zero));

        var result = await sut.SaveAsync(
            registrationId,
            new Dictionary<string, string> { ["co_libilo"] = "x" },
            FeedbackEditedBy.Self,
            null,
            CancellationToken.None);

        Assert.False(result.Persisted);
        Assert.Equal("NotOpen", result.RejectionReason);

        await using var verify = new ApplicationDbContext(options);
        Assert.False(await verify.FeedbackResponses.AnyAsync());
    }

    [Fact]
    public async Task SaveAsync_returns_Closed_when_window_passed()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        // Default close = FixedUtc + 62 d.
        var sut = CreateSut(options, new DateTimeOffset(FixedUtc.AddDays(80), TimeSpan.Zero));

        var result = await sut.SaveAsync(
            registrationId,
            new Dictionary<string, string> { ["co_libilo"] = "x" },
            FeedbackEditedBy.Self,
            null,
            CancellationToken.None);

        Assert.False(result.Persisted);
        Assert.Equal("Closed", result.RejectionReason);
    }

    [Fact]
    public async Task SaveAsync_throws_for_unknown_registration()
    {
        var options = CreateOptions();
        var sut = CreateSut(options, NowDuringWindow());

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SaveAsync(
            999,
            new Dictionary<string, string>(),
            FeedbackEditedBy.Self,
            null,
            CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_ignores_unknown_question_keys()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        var sut = CreateSut(options, NowDuringWindow());

        await sut.SaveAsync(
            registrationId,
            new Dictionary<string, string>
            {
                ["co_libilo"] = "kept",
                ["nonexistent_key"] = "dropped",
            },
            FeedbackEditedBy.Self,
            null,
            CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var response = await verify.FeedbackResponses.SingleAsync(x => x.RegistrationId == registrationId);
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(response.AnswersJson)!;
        Assert.Single(dict);
        Assert.Equal("kept", dict["co_libilo"]);
        Assert.False(dict.ContainsKey("nonexistent_key"));
    }

    [Fact]
    public async Task SaveAsync_skips_blank_answers_without_overwriting_existing()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        var sut = CreateSut(options, NowDuringWindow());

        // First save populates both keys.
        await sut.SaveAsync(
            registrationId,
            new Dictionary<string, string>
            {
                ["co_libilo"] = "krásné",
                ["co_zlepsit"] = "víc her",
            },
            FeedbackEditedBy.Self,
            null,
            CancellationToken.None);

        // Second save sends a blank for one. Merge semantics: blank value
        // must NOT overwrite the previously saved non-blank value; the other
        // key is updated normally.
        await sut.SaveAsync(
            registrationId,
            new Dictionary<string, string>
            {
                ["co_libilo"] = "ještě krásnější",
                ["co_zlepsit"] = "   ",
            },
            FeedbackEditedBy.Self,
            null,
            CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var response = await verify.FeedbackResponses.SingleAsync(x => x.RegistrationId == registrationId);
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(response.AnswersJson)!;
        Assert.Equal("ještě krásnější", dict["co_libilo"]);
        Assert.Equal("víc her", dict["co_zlepsit"]);
    }

    [Fact]
    public async Task SaveAsync_preserves_keys_not_mentioned_in_incoming_payload()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        var sut = CreateSut(options, NowDuringWindow());

        // First save mentions only co_libilo.
        await sut.SaveAsync(
            registrationId,
            new Dictionary<string, string> { ["co_libilo"] = "first" },
            FeedbackEditedBy.Self,
            null,
            CancellationToken.None);

        // Second save mentions only co_zlepsit; the previous co_libilo entry
        // must survive the merge (whole-blob replace would drop it).
        await sut.SaveAsync(
            registrationId,
            new Dictionary<string, string> { ["co_zlepsit"] = "second" },
            FeedbackEditedBy.Self,
            null,
            CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var response = await verify.FeedbackResponses.SingleAsync(x => x.RegistrationId == registrationId);
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(response.AnswersJson)!;
        Assert.Equal("first", dict["co_libilo"]);
        Assert.Equal("second", dict["co_zlepsit"]);
    }

    [Fact]
    public async Task SaveAsync_preserves_czech_diacritics()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        var sut = CreateSut(options, NowDuringWindow());

        const string original = "řeč žluťoučký kůň úpěl ďábelské ódy";

        await sut.SaveAsync(
            registrationId,
            new Dictionary<string, string> { ["co_libilo"] = original },
            FeedbackEditedBy.Self,
            null,
            CancellationToken.None);

        // Round-trip through GetViewAsync — the canonical user path.
        var view = await sut.GetViewAsync(registrationId, CancellationToken.None);
        Assert.NotNull(view);
        Assert.Equal(original, view!.Answers["co_libilo"]);

        // Bonus: the persisted JSON itself should not have escaped the
        // diacritics. Saves the next person who runs `SELECT AnswersJson FROM …`
        // from a wall of \u-escapes.
        await using var verify = new ApplicationDbContext(options);
        var raw = await verify.FeedbackResponses
            .Where(x => x.RegistrationId == registrationId)
            .Select(x => x.AnswersJson)
            .SingleAsync();
        Assert.Contains("řeč", raw);
    }

    // -------------------------------------------------------------------------
    // MarkSubmittedAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarkSubmittedAsync_sets_SubmittedAtUtc()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        var now = NowDuringWindow();
        var sut = CreateSut(options, now);

        await sut.SaveAsync(
            registrationId,
            new Dictionary<string, string> { ["co_libilo"] = "x" },
            FeedbackEditedBy.Self,
            null,
            CancellationToken.None);

        await sut.MarkSubmittedAsync(registrationId, CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var response = await verify.FeedbackResponses.SingleAsync(x => x.RegistrationId == registrationId);
        Assert.Equal(now, response.SubmittedAtUtc);
    }

    [Fact]
    public async Task MarkSubmittedAsync_is_idempotent()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        var firstNow = NowDuringWindow();
        var clock = new MutableTimeProvider(firstNow);
        var sut = new FeedbackService(
            new TestDbContextFactory(options),
            new FeedbackOptionsService(new TestDbContextFactory(options)),
            clock);

        await sut.SaveAsync(
            registrationId,
            new Dictionary<string, string> { ["co_libilo"] = "x" },
            FeedbackEditedBy.Self,
            null,
            CancellationToken.None);

        await sut.MarkSubmittedAsync(registrationId, CancellationToken.None);

        // Advance the clock and submit again.
        var secondNow = firstNow.AddHours(2);
        clock.Now = secondNow;
        await sut.MarkSubmittedAsync(registrationId, CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var response = await verify.FeedbackResponses.SingleAsync(x => x.RegistrationId == registrationId);
        Assert.Equal(firstNow, response.SubmittedAtUtc);
        Assert.Equal(secondNow, response.UpdatedAtUtc);
    }

    [Fact]
    public async Task MarkSubmittedAsync_throws_when_no_response_exists()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        var sut = CreateSut(options, NowDuringWindow());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.MarkSubmittedAsync(registrationId, CancellationToken.None));
    }

    [Fact]
    public async Task GetViewAsync_remains_editable_after_submission()
    {
        // SubmittedAtUtc is a dashboard status signal (Done vs Waiting), NOT
        // a lock on the form. The kid (or scribe) can keep editing after
        // pressing Hotovo because they may remember more the next morning.
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        var sut = CreateSut(options, NowDuringWindow());

        await sut.SaveAsync(
            registrationId,
            new Dictionary<string, string> { ["co_libilo"] = "x" },
            FeedbackEditedBy.Self,
            null,
            CancellationToken.None);
        await sut.MarkSubmittedAsync(registrationId, CancellationToken.None);

        var view = await sut.GetViewAsync(registrationId, CancellationToken.None);

        Assert.NotNull(view);
        Assert.False(view!.IsReadOnly);
        Assert.Null(view.ReadOnlyReason);
        Assert.NotNull(view.SubmittedAtUtc); // dashboard still sees "Done"
    }

    [Fact]
    public async Task SaveAsync_accepts_edits_after_submission()
    {
        // After Hotovo, additional saves must be persisted, and the original
        // SubmittedAtUtc must be preserved (it's the audit trail of "first
        // submit time", not a lock).
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        var firstNow = NowDuringWindow();
        var clock = new MutableTimeProvider(firstNow);
        var sut = new FeedbackService(
            new TestDbContextFactory(options),
            new FeedbackOptionsService(new TestDbContextFactory(options)),
            clock);

        await sut.SaveAsync(
            registrationId,
            new Dictionary<string, string> { ["co_libilo"] = "first answer" },
            FeedbackEditedBy.Self,
            null,
            CancellationToken.None);
        await sut.MarkSubmittedAsync(registrationId, CancellationToken.None);

        // Kid comes back the next morning with a better answer.
        clock.Now = firstNow.AddHours(20);
        var result = await sut.SaveAsync(
            registrationId,
            new Dictionary<string, string> { ["co_libilo"] = "remembered more" },
            FeedbackEditedBy.Self,
            null,
            CancellationToken.None);

        Assert.True(result.Persisted);
        Assert.Null(result.RejectionReason);

        await using var verify = new ApplicationDbContext(options);
        var response = await verify.FeedbackResponses.SingleAsync(x => x.RegistrationId == registrationId);
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(response.AnswersJson)!;
        Assert.Equal("remembered more", dict["co_libilo"]);
        // SubmittedAtUtc anchored to the first submit, not bumped by later edits.
        Assert.Equal(firstNow, response.SubmittedAtUtc);
    }

    // -------------------------------------------------------------------------
    // GetStatusAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetStatusAsync_returns_NotInvited_when_FeedbackInvitedAtUtc_is_null()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        var sut = CreateSut(options, NowDuringWindow());

        var status = await sut.GetStatusAsync(registrationId, CancellationToken.None);

        Assert.Equal(FeedbackStatus.NotInvited, status);
    }

    [Fact]
    public async Task GetStatusAsync_returns_Waiting_when_invited_but_not_submitted()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        await using (var db = new ApplicationDbContext(options))
        {
            var reg = await db.Registrations.SingleAsync(x => x.Id == registrationId);
            reg.FeedbackInvitedAtUtc = new DateTimeOffset(FixedUtc.AddDays(33), TimeSpan.Zero);
            await db.SaveChangesAsync();
        }
        var sut = CreateSut(options, NowDuringWindow());

        var status = await sut.GetStatusAsync(registrationId, CancellationToken.None);

        Assert.Equal(FeedbackStatus.Waiting, status);
    }

    [Fact]
    public async Task GetStatusAsync_returns_Done_when_SubmittedAtUtc_set()
    {
        var options = CreateOptions();
        var registrationId = await SeedRegistrationAsync(options, AttendeeType.Player);
        var sut = CreateSut(options, NowDuringWindow());

        await sut.SaveAsync(
            registrationId,
            new Dictionary<string, string> { ["co_libilo"] = "x" },
            FeedbackEditedBy.Self,
            null,
            CancellationToken.None);
        await sut.MarkSubmittedAsync(registrationId, CancellationToken.None);

        var status = await sut.GetStatusAsync(registrationId, CancellationToken.None);

        Assert.Equal(FeedbackStatus.Done, status);
    }

    [Fact]
    public async Task GetStatusAsync_returns_null_for_unknown_registration()
    {
        var options = CreateOptions();
        var sut = CreateSut(options, NowDuringWindow());

        var status = await sut.GetStatusAsync(999, CancellationToken.None);

        Assert.Null(status);
    }

    // -------------------------------------------------------------------------
    // helpers
    // -------------------------------------------------------------------------

    private static DateTimeOffset NowDuringWindow() =>
        // EndsAtUtc = FixedUtc + 32 d, default close = +30 d → safe midpoint.
        new(FixedUtc.AddDays(40), TimeSpan.Zero);

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static FeedbackService CreateSut(
        DbContextOptions<ApplicationDbContext> options,
        DateTimeOffset now)
    {
        var factory = new TestDbContextFactory(options);
        return new FeedbackService(
            factory,
            new FeedbackOptionsService(factory),
            new FixedTimeProvider(now));
    }

    private static async Task<int> SeedRegistrationAsync(
        DbContextOptions<ApplicationDbContext> options,
        AttendeeType attendeeType)
    {
        await using var db = new ApplicationDbContext(options);

        db.Users.Add(new ApplicationUser
        {
            Id = "user-1",
            Email = "parent@example.cz",
            NormalizedEmail = "PARENT@EXAMPLE.CZ",
            UserName = "parent@example.cz",
            NormalizedUserName = "PARENT@EXAMPLE.CZ",
            EmailConfirmed = true,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = FixedUtc,
        });
        db.Games.Add(new Game
        {
            Id = 1,
            Name = "Ovčina 2026",
            StartsAtUtc = FixedUtc.AddDays(30),
            EndsAtUtc = FixedUtc.AddDays(32),
            RegistrationClosesAtUtc = FixedUtc.AddDays(20),
            MealOrderingClosesAtUtc = FixedUtc.AddDays(15),
            PaymentDueAtUtc = FixedUtc.AddDays(25),
            PlayerBasePrice = 1200,
            AdultHelperBasePrice = 800,
            BankAccount = "x",
            BankAccountName = "y",
            VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
            IsPublished = true,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc,
            FeedbackKidQuestionsJson = KidJson,
            FeedbackAdultQuestionsJson = AdultJson,
        });
        db.RegistrationSubmissions.Add(new RegistrationSubmission
        {
            Id = 1,
            GameId = 1,
            RegistrantUserId = "user-1",
            PrimaryContactName = "Parent",
            PrimaryEmail = "parent@example.cz",
            PrimaryPhone = "777111222",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200,
        });
        db.People.Add(new Person
        {
            Id = 1,
            FirstName = "Kid",
            LastName = "One",
            BirthYear = attendeeType == AttendeeType.Player ? 2015 : 1985,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc,
        });
        db.Registrations.Add(new Registration
        {
            Id = 1,
            SubmissionId = 1,
            PersonId = 1,
            AttendeeType = attendeeType,
            Status = RegistrationStatus.Active,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc,
        });

        await db.SaveChangesAsync();
        return 1;
    }

    private static async Task SeedExistingResponseAsync(
        DbContextOptions<ApplicationDbContext> options,
        int registrationId,
        string answersJson)
    {
        await using var db = new ApplicationDbContext(options);
        db.FeedbackResponses.Add(new FeedbackResponse
        {
            RegistrationId = registrationId,
            AnswersJson = answersJson,
            LastEditedBy = FeedbackEditedBy.Self,
            CreatedAtUtc = new DateTimeOffset(FixedUtc.AddDays(33), TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(FixedUtc.AddDays(33), TimeSpan.Zero),
        });
        await db.SaveChangesAsync();
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => new(CreateDbContext());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = initial;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
