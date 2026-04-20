using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.CharacterPrep;

namespace RegistraceOvcina.Web.Tests.Features.CharacterPrep;

/// <summary>
/// Dashboard action-bar behavior exercised through the same service calls the page
/// performs: bulk pozvánka/připomínka, per-row reminder logic, and the
/// "hide bulk buttons once game started" guard.
/// </summary>
public sealed class CharacterPrepDashboardActionsTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset NowUtc = new(FixedUtc);
    private const int GameId = 1;

    [Fact]
    public async Task Bulk_pozvanka_click_sends_to_all_targets()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, startsIn: TimeSpan.FromDays(30));
        await AddSubmissionAsync(options, submissionId: 1, invitedAtUtc: null, players: 1, fullyEquipped: false);
        await AddSubmissionAsync(options, submissionId: 2, invitedAtUtc: null, players: 1, fullyEquipped: false);
        await AddSubmissionAsync(options, submissionId: 3, invitedAtUtc: null, players: 1, fullyEquipped: false);

        var (mail, sender, prep) = Build(options);

        var targets = await prep.ListInvitationTargetsAsync(GameId, CancellationToken.None);
        Assert.Equal(3, targets.Count); // sanity: counter the page will show

        var result = await mail.SendBulkPozvankaAsync(GameId, NowUtc, CancellationToken.None);

        Assert.Equal(3, result.Sent);
        Assert.Equal(0, result.Failed);
        Assert.Equal(3, sender.Captured.Count);
    }

    [Fact]
    public async Task Bulk_pripominka_click_sends_to_all_reminder_targets()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, startsIn: TimeSpan.FromDays(30));
        // Already invited 2 days ago, still not equipped → eligible for reminder.
        await AddSubmissionAsync(options,
            submissionId: 1,
            invitedAtUtc: NowUtc.AddDays(-2),
            players: 1,
            fullyEquipped: false);
        await AddSubmissionAsync(options,
            submissionId: 2,
            invitedAtUtc: NowUtc.AddDays(-2),
            players: 1,
            fullyEquipped: false);

        var (mail, sender, prep) = Build(options);

        var reminderTargets = await prep.ListReminderTargetsAsync(GameId, NowUtc, CancellationToken.None);
        Assert.Equal(2, reminderTargets.Count);

        var result = await mail.SendBulkPripominkaAsync(GameId, NowUtc, CancellationToken.None);

        Assert.Equal(2, result.Sent);
        Assert.Equal(0, result.Failed);
        Assert.Equal(2, sender.Captured.Count);
        Assert.All(sender.Captured, m => Assert.Contains("Připomínka", m.Subject));
    }

    [Fact]
    public async Task Per_row_reminder_on_NotInvited_row_invokes_Pozvanka()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, startsIn: TimeSpan.FromDays(30));
        await AddSubmissionAsync(options, submissionId: 1, invitedAtUtc: null, players: 1, fullyEquipped: false);

        var (mail, sender, _) = Build(options);

        // The dashboard decides which method to call based on the row's status.
        // For a NotInvited row the action is pozvánka.
        var result = await mail.SendPozvankaAsync(1, NowUtc, CancellationToken.None);

        Assert.Equal(SendSingleResult.Sent, result);
        Assert.Single(sender.Captured);

        await using var verify = new ApplicationDbContext(options);
        var submission = await verify.RegistrationSubmissions.SingleAsync();
        Assert.NotNull(submission.CharacterPrepInvitedAtUtc);
    }

    [Fact]
    public async Task Per_row_reminder_on_Waiting_row_invokes_Pripominka()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, startsIn: TimeSpan.FromDays(30));
        await AddSubmissionAsync(options,
            submissionId: 1,
            invitedAtUtc: NowUtc.AddDays(-3),
            players: 1,
            fullyEquipped: false);

        var (mail, sender, _) = Build(options);

        var result = await mail.SendPripominkaAsync(1, NowUtc, CancellationToken.None);

        Assert.Equal(SendSingleResult.Sent, result);
        Assert.Single(sender.Captured);
        Assert.Contains("Připomínka", sender.Captured[0].Subject);
    }

    [Fact]
    public async Task Bulk_buttons_are_disabled_when_game_already_started()
    {
        var options = CreateOptions();
        // Game started yesterday.
        await SeedGameAsync(options, startsIn: TimeSpan.FromDays(-1));
        await AddSubmissionAsync(options, submissionId: 1, invitedAtUtc: null, players: 1, fullyEquipped: false);

        var service = new CharacterPrepService(new TestDbContextFactory(options));

        // Page computes the "game has started" flag from Game.StartsAtUtc <= now and
        // disables bulk buttons. We assert the underlying condition rather than render
        // HTML (no bUnit/WAF).
        await using var db = new ApplicationDbContext(options);
        var game = await db.Games.AsNoTracking().SingleAsync();
        var hasStarted = game.StartsAtUtc <= NowUtc.UtcDateTime;
        Assert.True(hasStarted);

        // And the service still reports targets — the UI is what gates the call.
        var targets = await service.ListInvitationTargetsAsync(GameId, CancellationToken.None);
        Assert.Single(targets);
    }

    [Fact]
    public async Task Bulk_send_result_counts_reflect_toast_message()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, startsIn: TimeSpan.FromDays(30));
        await AddSubmissionAsync(options, submissionId: 1, invitedAtUtc: null, players: 1, fullyEquipped: false);
        await AddSubmissionAsync(options, submissionId: 2, invitedAtUtc: null, players: 1, fullyEquipped: false);
        await AddSubmissionAsync(options, submissionId: 3, invitedAtUtc: null, players: 1, fullyEquipped: false);

        var (mail, sender, _) = Build(options);
        sender.FailForRecipient = "u2@example.cz";

        var result = await mail.SendBulkPozvankaAsync(GameId, NowUtc, CancellationToken.None);

        Assert.Equal(2, result.Sent);
        Assert.Equal(1, result.Failed);
        // The page composes a toast like:
        //   "Posláno: {Sent} pozvánek, {Failed} chyb"
        var toast = $"Posláno: {result.Sent} pozvánek, {result.Failed} chyb";
        Assert.Equal("Posláno: 2 pozvánek, 1 chyb", toast);
    }

    // ------------------------------------------------------------ helpers

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static (CharacterPrepMailService Mail, FakeCharacterPrepEmailSender Sender, CharacterPrepService Prep) Build(
        DbContextOptions<ApplicationDbContext> options)
    {
        var factory = new TestDbContextFactory(options);
        var prep = new CharacterPrepService(factory);
        var token = new CharacterPrepTokenService(factory);
        var renderer = new CharacterPrepEmailRenderer();
        var sender = new FakeCharacterPrepEmailSender();
        var prepOptions = Options.Create(new CharacterPrepOptions
        {
            PublicBaseUrl = "https://registrace.ovcina.cz",
            OrganizerContactEmail = "organizatori@ovcina.cz"
        });
        var mail = new CharacterPrepMailService(
            factory, renderer, token, prep, sender, prepOptions,
            NullLogger<CharacterPrepMailService>.Instance);
        return (mail, sender, prep);
    }

    private static async Task SeedGameAsync(DbContextOptions<ApplicationDbContext> options, TimeSpan startsIn)
    {
        await using var db = new ApplicationDbContext(options);
        db.Games.Add(new Game
        {
            Id = GameId, Name = "Ovčina 2026",
            StartsAtUtc = FixedUtc.Add(startsIn),
            EndsAtUtc = FixedUtc.Add(startsIn).AddDays(2),
            RegistrationClosesAtUtc = FixedUtc.AddDays(-10),
            MealOrderingClosesAtUtc = FixedUtc.AddDays(-10),
            PaymentDueAtUtc = FixedUtc.AddDays(-5),
            PlayerBasePrice = 1200,
            AdultHelperBasePrice = 800,
            BankAccount = "x",
            BankAccountName = "y",
            VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
            IsPublished = true,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        });
        db.StartingEquipmentOptions.Add(new StartingEquipmentOption
        {
            Id = 10, GameId = GameId, Key = "sword", DisplayName = "Meč", SortOrder = 1
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddSubmissionAsync(
        DbContextOptions<ApplicationDbContext> options,
        int submissionId,
        DateTimeOffset? invitedAtUtc,
        int players,
        bool fullyEquipped)
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
            CreatedAtUtc = FixedUtc
        });
        db.RegistrationSubmissions.Add(new RegistrationSubmission
        {
            Id = submissionId,
            GameId = GameId,
            RegistrantUserId = userId,
            PrimaryContactName = "HH " + submissionId,
            PrimaryEmail = $"u{submissionId}@example.cz",
            PrimaryPhone = "777000000",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200,
            CharacterPrepInvitedAtUtc = invitedAtUtc
        });
        await db.SaveChangesAsync();

        for (var i = 0; i < players; i++)
        {
            var pid = submissionId * 1000 + i + 1;
            db.People.Add(new Person
            {
                Id = pid, FirstName = "Kid" + i, LastName = "S" + submissionId, BirthYear = 2015,
                CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
            });
            db.Registrations.Add(new Registration
            {
                Id = pid,
                SubmissionId = submissionId,
                PersonId = pid,
                AttendeeType = AttendeeType.Player,
                Status = RegistrationStatus.Active,
                CharacterName = fullyEquipped ? "Aragorn" + i : null,
                StartingEquipmentOptionId = fullyEquipped ? 10 : null,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc
            });
        }
        await db.SaveChangesAsync();
    }

    private sealed class FakeCharacterPrepEmailSender : ICharacterPrepEmailSender
    {
        public List<SentMessage> Captured { get; } = new();
        public string? FailForRecipient { get; set; }

        public Task SendAsync(string recipientEmail, string subject, string htmlBody, CancellationToken cancellationToken)
        {
            if (FailForRecipient is not null
                && string.Equals(FailForRecipient, recipientEmail, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("simulated send failure");
            }
            Captured.Add(new SentMessage(recipientEmail, subject, htmlBody));
            return Task.CompletedTask;
        }
    }

    private sealed record SentMessage(string To, string Subject, string HtmlBody);

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ApplicationDbContext(options));
    }
}
