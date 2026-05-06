using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Feedback;

namespace RegistraceOvcina.Web.Tests.Features.Feedback;

/// <summary>
/// Covers <see cref="FeedbackMailService"/> — bulk orchestration of
/// invitation / reminder envelopes for post-game feedback.
/// <para>Test infrastructure mirrors <c>FeedbackServiceTests</c> +
/// <c>CharacterPrepMailServiceTests</c>: in-memory DB, private
/// <c>TestDbContextFactory</c>, <c>FixedTimeProvider</c>, capturing sender
/// double.</para>
/// </summary>
public sealed class FeedbackMailServiceTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset NowUtc = new(FixedUtc.AddDays(40), TimeSpan.Zero);
    private const int GameId = 1;

    private const string KidJson = """[{"key":"co_libilo","label":"Co se ti líbilo?"}]""";
    private const string AdultJson = """[{"key":"organizace","label":"Organizace?"}]""";

    // -------------------------------------------------------- SendInvitationsAsync

    [Fact]
    public async Task SendInvitationsAsync_returns_zero_for_no_targets()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        var (service, sender) = CreateService(options);

        var result = await service.SendInvitationsAsync(GameId, CancellationToken.None);

        Assert.Equal(0, result.BundleEmailsSent);
        Assert.Equal(0, result.IndividualEmailsSent);
        Assert.Equal(0, result.ErrorsLogged);
        Assert.Empty(sender.Captured);
    }

    [Fact]
    public async Task SendInvitationsAsync_sends_bundle_for_kid_only_submission()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, players: 2, adultsWithEmail: 0, adultsWithoutEmail: 0);
        var (service, sender) = CreateService(options);

        var result = await service.SendInvitationsAsync(GameId, CancellationToken.None);

        Assert.Equal(1, result.BundleEmailsSent);
        Assert.Equal(0, result.IndividualEmailsSent);
        Assert.Equal(0, result.ErrorsLogged);
        Assert.Single(sender.Captured);
        Assert.Equal("contact-1@example.cz", sender.Captured[0].To);
    }

    [Fact]
    public async Task SendInvitationsAsync_sends_individual_for_adult_with_email()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, players: 0, adultsWithEmail: 1, adultsWithoutEmail: 0);
        var (service, sender) = CreateService(options);

        var result = await service.SendInvitationsAsync(GameId, CancellationToken.None);

        Assert.Equal(0, result.BundleEmailsSent);
        Assert.Equal(1, result.IndividualEmailsSent);
        Assert.Equal(0, result.ErrorsLogged);
        Assert.Single(sender.Captured);
        // Adult-with-email is keyed off Person.Email, not the household contact.
        Assert.Equal("adult-with-email-1-0@example.cz", sender.Captured[0].To);
    }

    [Fact]
    public async Task SendInvitationsAsync_sends_both_when_submission_has_kids_and_adult_with_email()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, players: 1, adultsWithEmail: 1, adultsWithoutEmail: 0);
        var (service, sender) = CreateService(options);

        var result = await service.SendInvitationsAsync(GameId, CancellationToken.None);

        Assert.Equal(1, result.BundleEmailsSent);
        Assert.Equal(1, result.IndividualEmailsSent);
        Assert.Equal(0, result.ErrorsLogged);
        Assert.Equal(2, sender.Captured.Count);
        Assert.Contains(sender.Captured, c => c.To == "contact-1@example.cz");
        Assert.Contains(sender.Captured, c => c.To == "adult-with-email-1-0@example.cz");
    }

    [Fact]
    public async Task SendInvitationsAsync_skips_submission_when_no_contact_email_and_bundle_needed()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        // Player + blank PrimaryEmail → bundle is required but undeliverable.
        await AddSubmissionAsync(
            options,
            submissionId: 1,
            players: 1,
            adultsWithEmail: 0,
            adultsWithoutEmail: 0,
            primaryEmail: "");
        var (service, sender) = CreateService(options);

        var result = await service.SendInvitationsAsync(GameId, CancellationToken.None);

        Assert.Equal(0, result.BundleEmailsSent);
        Assert.Equal(0, result.IndividualEmailsSent);
        Assert.Equal(1, result.ErrorsLogged);
        Assert.Empty(sender.Captured);

        await using var verify = new ApplicationDbContext(options);
        // Did NOT mark the kid invited even though we hit this submission.
        var reg = await verify.Registrations.SingleAsync();
        Assert.Null(reg.FeedbackInvitedAtUtc);
    }

    [Fact]
    public async Task SendInvitationsAsync_marks_each_Registration_invited_after_successful_send()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, players: 2, adultsWithEmail: 1, adultsWithoutEmail: 0);
        var (service, _) = CreateService(options);

        var result = await service.SendInvitationsAsync(GameId, CancellationToken.None);

        Assert.Equal(1, result.BundleEmailsSent);
        Assert.Equal(1, result.IndividualEmailsSent);

        await using var verify = new ApplicationDbContext(options);
        var regs = await verify.Registrations.ToListAsync();
        Assert.All(regs, r => Assert.NotNull(r.FeedbackInvitedAtUtc));
        // Reminder timestamp must NOT be set by an invitation send.
        Assert.All(regs, r => Assert.Null(r.FeedbackReminderLastSentAtUtc));
    }

    [Fact]
    public async Task SendInvitationsAsync_does_not_mark_when_sender_throws()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, players: 1, adultsWithEmail: 0, adultsWithoutEmail: 0);
        var (service, sender) = CreateService(options);
        sender.ThrowOnEverySend = true;

        var result = await service.SendInvitationsAsync(GameId, CancellationToken.None);

        Assert.Equal(0, result.BundleEmailsSent);
        Assert.Equal(0, result.IndividualEmailsSent);
        Assert.Equal(1, result.ErrorsLogged);

        await using var verify = new ApplicationDbContext(options);
        var reg = await verify.Registrations.SingleAsync();
        Assert.Null(reg.FeedbackInvitedAtUtc);
    }

    // -------------------------------------------------------- SendRemindersAsync

    [Fact]
    public async Task SendRemindersAsync_targets_only_waiting_with_no_recent_reminder()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);

        // S1: invited 3 days ago, no reminder → should target.
        await AddSubmissionAsync(
            options, submissionId: 1, players: 1, adultsWithEmail: 0, adultsWithoutEmail: 0,
            invitedAtUtc: NowUtc.AddDays(-3), reminderLastSentAtUtc: null);
        // S2: invited 3 days ago, reminded 23h ago → throttled out.
        await AddSubmissionAsync(
            options, submissionId: 2, players: 1, adultsWithEmail: 0, adultsWithoutEmail: 0,
            invitedAtUtc: NowUtc.AddDays(-3), reminderLastSentAtUtc: NowUtc.AddHours(-23));
        // S3: never invited → not a reminder target at all.
        await AddSubmissionAsync(
            options, submissionId: 3, players: 1, adultsWithEmail: 0, adultsWithoutEmail: 0,
            invitedAtUtc: null);

        var (service, sender) = CreateService(options);

        var result = await service.SendRemindersAsync(GameId, CancellationToken.None);

        Assert.Equal(1, result.BundleEmailsSent);
        Assert.Equal(0, result.IndividualEmailsSent);
        Assert.Equal(0, result.ErrorsLogged);
        Assert.Single(sender.Captured);
        Assert.Equal("contact-1@example.cz", sender.Captured[0].To);
    }

    [Fact]
    public async Task SendRemindersAsync_marks_FeedbackReminderLastSentAtUtc_after_send()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(
            options, submissionId: 1, players: 1, adultsWithEmail: 0, adultsWithoutEmail: 0,
            invitedAtUtc: NowUtc.AddDays(-3), reminderLastSentAtUtc: null);
        var (service, _) = CreateService(options);

        var result = await service.SendRemindersAsync(GameId, CancellationToken.None);

        Assert.Equal(1, result.BundleEmailsSent);

        await using var verify = new ApplicationDbContext(options);
        var reg = await verify.Registrations.SingleAsync();
        Assert.NotNull(reg.FeedbackReminderLastSentAtUtc);
        Assert.Equal(NowUtc, reg.FeedbackReminderLastSentAtUtc);
        // Original invite stamp must remain untouched.
        Assert.Equal(NowUtc.AddDays(-3), reg.FeedbackInvitedAtUtc);
    }

    [Fact]
    public async Task SendInvitationsAsync_ensures_token_for_each_targeted_registration()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, players: 2, adultsWithEmail: 1, adultsWithoutEmail: 0);
        var (service, _) = CreateService(options);

        await service.SendInvitationsAsync(GameId, CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var regs = await verify.Registrations.ToListAsync();
        Assert.Equal(3, regs.Count);
        Assert.All(regs, r =>
        {
            Assert.NotNull(r.FeedbackToken);
            Assert.NotEqual(Guid.Empty, r.FeedbackToken!.Value);
        });
        // Tokens must be unique per registration (lazy-issue + never-rotate).
        var distinct = regs.Select(r => r.FeedbackToken).Distinct().Count();
        Assert.Equal(3, distinct);
    }

    // -------------------------------------------------- SendTest* methods
    //
    // Test-send now uses the logged-in user's REAL submission/registration
    // data rather than dummy fixtures: bundle test resolves the user's own
    // submission for this game, adult-individual resolves a Person.Email
    // match. Tokens are issued lazily via FeedbackTokenService.EnsureTokenAsync
    // and the test path must NOT mark anyone invited.

    private const string TestUserId = "user-1";
    private const string TestUserEmail = "tom@example.cz";

    [Fact]
    public async Task SendTestBundleAsync_renders_through_existing_renderer_with_real_submission()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        // Seed a submission owned by TestUserId with two players. Submission
        // PrimaryContactName flows into the {ContactName} token.
        await AddSubmissionAsync(options, submissionId: 1, players: 2, adultsWithEmail: 0, adultsWithoutEmail: 0);
        await using (var db = new ApplicationDbContext(options))
        {
            var game = await db.Games.SingleAsync();
            game.FeedbackBundleSubjectTemplate = "TEST-{GameName}";
            game.FeedbackBundleHtmlTemplate = "<p>Hello {ContactName}!</p>{Entries}";
            await db.SaveChangesAsync();
        }
        var (service, sender) = CreateService(options);

        await service.SendTestBundleAsync(
            GameId, TestUserId, TestUserEmail, isReminder: false, CancellationToken.None);

        var captured = Assert.Single(sender.Captured);
        Assert.Equal(TestUserEmail, captured.To);
        Assert.Equal("TEST-Ovčina 2026", captured.Subject);
        // Real Person names from the seed (see AddSubmissionAsync).
        Assert.Contains("Kid0 S1", captured.HtmlBody);
        Assert.Contains("Kid1 S1", captured.HtmlBody);
        // Real PrimaryContactName from the seed.
        Assert.Contains("Contact 1", captured.HtmlBody);
    }

    [Fact]
    public async Task SendTestBundleAsync_throws_when_user_has_no_submission_in_game()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        // No submission seeded for TestUserId — the resolver should refuse.
        var (service, sender) = CreateService(options);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendTestBundleAsync(
                GameId, TestUserId, TestUserEmail, isReminder: false, CancellationToken.None));
        Assert.Contains("vlastní přihlášku", ex.Message);
        Assert.Empty(sender.Captured);
    }

    [Fact]
    public async Task SendTestBundleAsync_does_not_mark_anyone_invited()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, players: 2, adultsWithEmail: 0, adultsWithoutEmail: 0);
        var (service, _) = CreateService(options);

        await service.SendTestBundleAsync(
            GameId, TestUserId, TestUserEmail, isReminder: false, CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var regs = await verify.Registrations.ToListAsync();
        Assert.NotEmpty(regs);
        // Test-send must NOT touch the dashboard's "Pozváno" / reminder counters.
        Assert.All(regs, r => Assert.Null(r.FeedbackInvitedAtUtc));
        Assert.All(regs, r => Assert.Null(r.FeedbackReminderLastSentAtUtc));
    }

    [Fact]
    public async Task SendTestBundleAsync_issues_tokens_on_unminted_registrations()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, players: 2, adultsWithEmail: 0, adultsWithoutEmail: 0);
        // Sanity: tokens should start out null.
        await using (var pre = new ApplicationDbContext(options))
        {
            Assert.All(await pre.Registrations.ToListAsync(), r => Assert.Null(r.FeedbackToken));
        }
        var (service, sender) = CreateService(options);

        await service.SendTestBundleAsync(
            GameId, TestUserId, TestUserEmail, isReminder: false, CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var regs = await verify.Registrations.ToListAsync();
        Assert.All(regs, r =>
        {
            Assert.NotNull(r.FeedbackToken);
            Assert.NotEqual(Guid.Empty, r.FeedbackToken!.Value);
        });
        // The captured HTML must reference each freshly issued token (proves the
        // entries are wired off the real ids, not random Guids).
        var captured = Assert.Single(sender.Captured);
        foreach (var reg in regs)
        {
            Assert.Contains(reg.FeedbackToken!.Value.ToString(), captured.HtmlBody);
        }
    }

    [Fact]
    public async Task SendTestAdultIndividualAsync_renders_with_real_registration()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        // Seed a submission for TestUserId then add one adult Person whose
        // Email matches TestUserEmail — that's how the service maps the
        // logged-in user to a Registration.
        await AddSubmissionAsync(options, submissionId: 1, players: 0, adultsWithEmail: 0, adultsWithoutEmail: 0);
        await using (var db = new ApplicationDbContext(options))
        {
            db.People.Add(new Person
            {
                Id = 9001,
                FirstName = "Tomáš",
                LastName = "Pajonk",
                BirthYear = 1985,
                Email = TestUserEmail,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc,
            });
            db.Registrations.Add(new Registration
            {
                Id = 9001,
                SubmissionId = 1,
                PersonId = 9001,
                AttendeeType = AttendeeType.Adult,
                Status = RegistrationStatus.Active,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc,
            });
            var game = await db.Games.SingleAsync();
            game.FeedbackAdultIndividualSubjectTemplate = "ADULT-{GameName}";
            game.FeedbackAdultIndividualHtmlTemplate = "<p>Ahoj {AttendeeName}, link: {TokenLink}</p>";
            await db.SaveChangesAsync();
        }
        var (service, sender) = CreateService(options);

        await service.SendTestAdultIndividualAsync(
            GameId, TestUserId, TestUserEmail, isReminder: false, CancellationToken.None);

        var captured = Assert.Single(sender.Captured);
        Assert.Equal(TestUserEmail, captured.To);
        Assert.Equal("ADULT-Ovčina 2026", captured.Subject);
        // Real attendee name from the seeded Person.
        Assert.Contains("Tomáš Pajonk", captured.HtmlBody);
        Assert.Contains("https://registrace.ovcina.cz/zpetna-vazba/", captured.HtmlBody);

        // Token must have been minted on the seeded Registration.
        await using var verify = new ApplicationDbContext(options);
        var reg = await verify.Registrations.SingleAsync(r => r.Id == 9001);
        Assert.NotNull(reg.FeedbackToken);
        Assert.Contains(reg.FeedbackToken!.Value.ToString(), captured.HtmlBody);
        // No invited-marking side effect.
        Assert.Null(reg.FeedbackInvitedAtUtc);
    }

    [Fact]
    public async Task SendTestAdultIndividualAsync_throws_when_user_has_no_adult_registration_in_game()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        // Submission with only kids — no adult registration matches the user's email.
        await AddSubmissionAsync(options, submissionId: 1, players: 1, adultsWithEmail: 0, adultsWithoutEmail: 0);
        var (service, sender) = CreateService(options);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendTestAdultIndividualAsync(
                GameId, TestUserId, TestUserEmail, isReminder: false, CancellationToken.None));
        Assert.Contains("zaregistrováni jako dospělý", ex.Message);
        Assert.Empty(sender.Captured);
    }

    [Fact]
    public async Task SendTestBundleAsync_throws_when_toEmail_is_blank()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        var (service, sender) = CreateService(options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendTestBundleAsync(GameId, TestUserId, "", isReminder: false, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendTestBundleAsync(GameId, TestUserId, "   ", isReminder: false, CancellationToken.None));

        Assert.Empty(sender.Captured);
    }

    [Fact]
    public async Task SendTestBundleAsync_throws_when_game_does_not_exist()
    {
        var options = CreateOptions();
        // No game seeded.
        var (service, sender) = CreateService(options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendTestBundleAsync(
                GameId, TestUserId, TestUserEmail, isReminder: false, CancellationToken.None));

        Assert.Empty(sender.Captured);
    }

    [Fact]
    public async Task SendTestBundleAsync_uses_isReminder_flag()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, players: 1, adultsWithEmail: 0, adultsWithoutEmail: 0);
        var (service, sender) = CreateService(options);

        await service.SendTestBundleAsync(
            GameId, TestUserId, TestUserEmail, isReminder: true, CancellationToken.None);

        var captured = Assert.Single(sender.Captured);
        // Default reminder subject (no template override) starts with "Připomínka:".
        Assert.StartsWith("Připomínka:", captured.Subject);
        // Default reminder body includes the gentle reminder phrasing.
        Assert.Contains("tichou připomínku", captured.HtmlBody);
    }

    [Fact]
    public async Task SendTestAdultIndividualAsync_throws_when_toEmail_is_blank()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        var (service, sender) = CreateService(options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendTestAdultIndividualAsync(
                GameId, TestUserId, "", isReminder: false, CancellationToken.None));

        Assert.Empty(sender.Captured);
    }

    // ------------------------------------------------------------------ helpers

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static (FeedbackMailService Service, FakeFeedbackEmailSender Sender) CreateService(
        DbContextOptions<ApplicationDbContext> options)
    {
        var factory = new TestDbContextFactory(options);
        var optionsService = new FeedbackOptionsService(factory);
        var feedbackService = new FeedbackService(factory, optionsService, new FixedTimeProvider(NowUtc));
        var tokenService = new FeedbackTokenService(factory);
        var renderer = new FeedbackEmailRenderer();
        var sender = new FakeFeedbackEmailSender();
        var feedbackOptions = Options.Create(new FeedbackOptions
        {
            PublicBaseUrl = "https://registrace.ovcina.cz",
            OrganizerContactEmail = "organizatori@ovcina.cz",
        });

        var mailService = new FeedbackMailService(
            factory,
            feedbackService,
            tokenService,
            renderer,
            sender,
            feedbackOptions,
            new FixedTimeProvider(NowUtc),
            NullLogger<FeedbackMailService>.Instance);

        return (mailService, sender);
    }

    private static async Task SeedGameAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);
        db.Games.Add(new Game
        {
            Id = GameId,
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
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Adds a submission with a given mix of players (kids), adults with their
    /// own email, and adults without email. Players + adults-without-email
    /// route to the household bundle; adults-with-email each get their own
    /// envelope.
    /// </summary>
    private static async Task AddSubmissionAsync(
        DbContextOptions<ApplicationDbContext> options,
        int submissionId,
        int players,
        int adultsWithEmail,
        int adultsWithoutEmail,
        DateTimeOffset? invitedAtUtc = null,
        DateTimeOffset? reminderLastSentAtUtc = null,
        string? primaryEmail = null)
    {
        await using var db = new ApplicationDbContext(options);
        var userId = "user-" + submissionId;
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            DisplayName = "U" + submissionId,
            Email = $"u{submissionId}@example.cz",
            NormalizedEmail = $"U{submissionId}@EXAMPLE.CZ",
            UserName = $"u{submissionId}@example.cz",
            NormalizedUserName = $"U{submissionId}@EXAMPLE.CZ",
            EmailConfirmed = true,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = FixedUtc,
        });
        db.RegistrationSubmissions.Add(new RegistrationSubmission
        {
            Id = submissionId,
            GameId = GameId,
            RegistrantUserId = userId,
            PrimaryContactName = "Contact " + submissionId,
            PrimaryEmail = primaryEmail ?? $"contact-{submissionId}@example.cz",
            PrimaryPhone = "777000000",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200,
        });

        var personBase = submissionId * 1000;
        var regBase = submissionId * 1000;
        var slot = 0;

        for (var i = 0; i < players; i++, slot++)
        {
            var pid = personBase + slot + 1;
            db.People.Add(new Person
            {
                Id = pid,
                FirstName = "Kid" + i,
                LastName = "S" + submissionId,
                BirthYear = 2015,
                Email = null,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc,
            });
            db.Registrations.Add(new Registration
            {
                Id = regBase + slot + 1,
                SubmissionId = submissionId,
                PersonId = pid,
                AttendeeType = AttendeeType.Player,
                Status = RegistrationStatus.Active,
                FeedbackInvitedAtUtc = invitedAtUtc,
                FeedbackReminderLastSentAtUtc = reminderLastSentAtUtc,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc,
            });
        }
        for (var i = 0; i < adultsWithEmail; i++, slot++)
        {
            var pid = personBase + slot + 1;
            db.People.Add(new Person
            {
                Id = pid,
                FirstName = "AdultE" + i,
                LastName = "S" + submissionId,
                BirthYear = 1985,
                Email = $"adult-with-email-{submissionId}-{i}@example.cz",
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc,
            });
            db.Registrations.Add(new Registration
            {
                Id = regBase + slot + 1,
                SubmissionId = submissionId,
                PersonId = pid,
                AttendeeType = AttendeeType.Adult,
                Status = RegistrationStatus.Active,
                FeedbackInvitedAtUtc = invitedAtUtc,
                FeedbackReminderLastSentAtUtc = reminderLastSentAtUtc,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc,
            });
        }
        for (var i = 0; i < adultsWithoutEmail; i++, slot++)
        {
            var pid = personBase + slot + 1;
            db.People.Add(new Person
            {
                Id = pid,
                FirstName = "AdultN" + i,
                LastName = "S" + submissionId,
                BirthYear = 1985,
                Email = null,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc,
            });
            db.Registrations.Add(new Registration
            {
                Id = regBase + slot + 1,
                SubmissionId = submissionId,
                PersonId = pid,
                AttendeeType = AttendeeType.Adult,
                Status = RegistrationStatus.Active,
                FeedbackInvitedAtUtc = invitedAtUtc,
                FeedbackReminderLastSentAtUtc = reminderLastSentAtUtc,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc,
            });
        }

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

    /// <summary>
    /// Captures every send call without touching Graph; can be flipped to
    /// throw so the per-submission error path is exercisable.
    /// </summary>
    private sealed class FakeFeedbackEmailSender : IFeedbackEmailSender
    {
        public List<SentMessage> Captured { get; } = new();
        public bool ThrowOnEverySend { get; set; }

        public Task SendAsync(string recipientEmail, string subject, string htmlBody, CancellationToken cancellationToken)
        {
            if (ThrowOnEverySend)
            {
                throw new InvalidOperationException("simulated send failure");
            }
            Captured.Add(new SentMessage(recipientEmail, subject, htmlBody));
            return Task.CompletedTask;
        }
    }

    private sealed record SentMessage(string To, string Subject, string HtmlBody);
}
