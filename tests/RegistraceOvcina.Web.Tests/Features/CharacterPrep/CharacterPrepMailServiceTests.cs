using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.CharacterPrep;

namespace RegistraceOvcina.Web.Tests.Features.CharacterPrep;

public sealed class CharacterPrepMailServiceTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset NowUtc = new(FixedUtc);
    private const int GameId = 1;
    private const int EquipmentId = 10;

    [Fact]
    public async Task SendPozvankaAsync_first_time_sends_email_and_marks_invited()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, invitedAtUtc: null, players: 1, adults: 0);

        var (service, sender) = CreateService(options);

        var result = await service.SendPozvankaAsync(1, NowUtc, CancellationToken.None);

        Assert.Equal(SendSingleResult.Sent, result);
        Assert.Single(sender.Captured);
        Assert.Equal("u1@example.cz", sender.Captured[0].To);

        await using var verify = new ApplicationDbContext(options);
        var submission = await verify.RegistrationSubmissions.SingleAsync(x => x.Id == 1);
        Assert.NotNull(submission.CharacterPrepInvitedAtUtc);
        Assert.False(string.IsNullOrEmpty(submission.CharacterPrepToken));
    }

    [Fact]
    public async Task SendPozvankaAsync_second_time_returns_AlreadyInvited_no_duplicate_sends()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(
            options, submissionId: 1,
            invitedAtUtc: NowUtc.AddDays(-2),
            players: 1, adults: 0);

        var (service, sender) = CreateService(options);

        var result = await service.SendPozvankaAsync(1, NowUtc, CancellationToken.None);

        Assert.Equal(SendSingleResult.AlreadyInvited, result);
        Assert.Empty(sender.Captured);
    }

    [Fact]
    public async Task SendPozvankaAsync_writes_EmailMessage_outbox_row_with_LinkedSubmissionId()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, invitedAtUtc: null, players: 1, adults: 0);

        var (service, _) = CreateService(options);

        await service.SendPozvankaAsync(1, NowUtc, CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var outbox = await verify.EmailMessages.Where(x => x.LinkedSubmissionId == 1).ToListAsync();
        Assert.Single(outbox);
        Assert.Equal(EmailDirection.Outbound, outbox[0].Direction);
        Assert.Equal("u1@example.cz", outbox[0].To);
        Assert.Contains("Ovčina 2026", outbox[0].Subject);
        Assert.False(string.IsNullOrEmpty(outbox[0].BodyText));
    }

    [Fact]
    public async Task SendPozvankaAsync_missing_recipient_email_returns_MissingRecipient()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(
            options, submissionId: 1,
            invitedAtUtc: null,
            players: 1, adults: 0,
            primaryEmail: "");

        var (service, sender) = CreateService(options);

        var result = await service.SendPozvankaAsync(1, NowUtc, CancellationToken.None);

        Assert.Equal(SendSingleResult.MissingRecipient, result);
        Assert.Empty(sender.Captured);
    }

    [Fact]
    public async Task SendPripominkaAsync_without_prior_invite_returns_Error()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, invitedAtUtc: null, players: 1, adults: 0);

        var (service, sender) = CreateService(options);

        var result = await service.SendPripominkaAsync(1, NowUtc, CancellationToken.None);

        Assert.Equal(SendSingleResult.Error, result);
        Assert.Empty(sender.Captured);
    }

    [Fact]
    public async Task SendPripominkaAsync_within_24h_returns_ThrottledSkipped_no_send()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(
            options, submissionId: 1,
            invitedAtUtc: NowUtc.AddDays(-3),
            players: 1, adults: 0,
            reminderLastSentAtUtc: NowUtc.AddHours(-23));

        var (service, sender) = CreateService(options);

        var result = await service.SendPripominkaAsync(1, NowUtc, CancellationToken.None);

        Assert.Equal(SendSingleResult.ThrottledSkipped, result);
        Assert.Empty(sender.Captured);
    }

    [Fact]
    public async Task SendPripominkaAsync_after_24h_sends_and_marks_reminder()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(
            options, submissionId: 1,
            invitedAtUtc: NowUtc.AddDays(-3),
            players: 1, adults: 0,
            reminderLastSentAtUtc: NowUtc.AddHours(-25));

        var (service, sender) = CreateService(options);

        var result = await service.SendPripominkaAsync(1, NowUtc, CancellationToken.None);

        Assert.Equal(SendSingleResult.Sent, result);
        Assert.Single(sender.Captured);
        Assert.Contains("Připomínka", sender.Captured[0].Subject);

        await using var verify = new ApplicationDbContext(options);
        var submission = await verify.RegistrationSubmissions.SingleAsync(x => x.Id == 1);
        Assert.Equal(NowUtc, submission.CharacterPrepReminderLastSentAtUtc);
    }

    [Fact]
    public async Task SendBulkPozvankaAsync_iterates_all_targets_and_counts_sent_and_failed()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, invitedAtUtc: null, players: 1, adults: 0);
        await AddSubmissionAsync(options, submissionId: 2, invitedAtUtc: null, players: 1, adults: 0);
        await AddSubmissionAsync(options, submissionId: 3, invitedAtUtc: null, players: 1, adults: 0);

        var (service, sender) = CreateService(options);
        // Fail the second recipient only — other two should still go through.
        sender.FailForRecipient = "u2@example.cz";

        var result = await service.SendBulkPozvankaAsync(GameId, NowUtc, CancellationToken.None);

        Assert.Equal(2, result.Sent);
        Assert.Equal(1, result.Failed);
        Assert.NotNull(result.FirstError);

        await using var verify = new ApplicationDbContext(options);
        var invited = await verify.RegistrationSubmissions
            .Where(x => x.CharacterPrepInvitedAtUtc != null).CountAsync();
        Assert.Equal(2, invited);
    }

    [Fact]
    public async Task SendBulkPripominkaAsync_skips_submissions_without_empty_equipment()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        // Already fully equipped — not a reminder target.
        await AddSubmissionAsync(
            options, submissionId: 1,
            invitedAtUtc: NowUtc.AddDays(-3),
            players: 1, adults: 0,
            allPlayersEquipped: true,
            reminderLastSentAtUtc: null);
        // Unequipped + invited + past 24h — should receive reminder.
        await AddSubmissionAsync(
            options, submissionId: 2,
            invitedAtUtc: NowUtc.AddDays(-3),
            players: 1, adults: 0,
            allPlayersEquipped: false,
            reminderLastSentAtUtc: NowUtc.AddHours(-25));

        var (service, sender) = CreateService(options);

        var result = await service.SendBulkPripominkaAsync(GameId, NowUtc, CancellationToken.None);

        Assert.Equal(1, result.Sent);
        Assert.Equal(0, result.Failed);
        Assert.Single(sender.Captured);
        Assert.Equal("u2@example.cz", sender.Captured[0].To);
    }

    // ------------------------------------------------------------ helpers

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static (CharacterPrepMailService Service, FakeCharacterPrepEmailSender Sender) CreateService(
        DbContextOptions<ApplicationDbContext> options)
    {
        var factory = new TestDbContextFactory(options);
        var prepService = new CharacterPrepService(factory);
        var tokenService = new CharacterPrepTokenService(factory);
        var renderer = new CharacterPrepEmailRenderer();
        var sender = new FakeCharacterPrepEmailSender();
        var mailOptions = Options.Create(new CharacterPrepOptions
        {
            PublicBaseUrl = "https://registrace.ovcina.cz",
            OrganizerContactEmail = "organizatori@ovcina.cz"
        });
        var mailService = new CharacterPrepMailService(
            factory,
            renderer,
            tokenService,
            prepService,
            sender,
            mailOptions,
            NullLogger<CharacterPrepMailService>.Instance);
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
            UpdatedAtUtc = FixedUtc
        });
        db.StartingEquipmentOptions.Add(new StartingEquipmentOption
        {
            Id = EquipmentId,
            GameId = GameId,
            Key = "tesak",
            DisplayName = "Tesák",
            SortOrder = 1
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddSubmissionAsync(
        DbContextOptions<ApplicationDbContext> options,
        int submissionId,
        DateTimeOffset? invitedAtUtc,
        int players,
        int adults,
        bool allPlayersEquipped = false,
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
            CreatedAtUtc = FixedUtc
        });
        db.RegistrationSubmissions.Add(new RegistrationSubmission
        {
            Id = submissionId,
            GameId = GameId,
            RegistrantUserId = userId,
            PrimaryContactName = "Contact " + submissionId,
            PrimaryEmail = primaryEmail ?? $"u{submissionId}@example.cz",
            PrimaryPhone = "777000000",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200,
            CharacterPrepInvitedAtUtc = invitedAtUtc,
            CharacterPrepReminderLastSentAtUtc = reminderLastSentAtUtc
        });
        var personBaseId = submissionId * 1000;
        var regBaseId = submissionId * 1000;
        for (var i = 0; i < players; i++)
        {
            var pid = personBaseId + i + 1;
            db.People.Add(new Person
            {
                Id = pid,
                FirstName = "Player" + i,
                LastName = "S" + submissionId,
                BirthYear = 2015,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc
            });
            db.Registrations.Add(new Registration
            {
                Id = regBaseId + i + 1,
                SubmissionId = submissionId,
                PersonId = pid,
                AttendeeType = AttendeeType.Player,
                Status = RegistrationStatus.Active,
                CharacterName = allPlayersEquipped ? "Hero " + i : null,
                StartingEquipmentOptionId = allPlayersEquipped ? EquipmentId : null,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc
            });
        }
        for (var i = 0; i < adults; i++)
        {
            var pid = personBaseId + 500 + i + 1;
            db.People.Add(new Person
            {
                Id = pid,
                FirstName = "Adult" + i,
                LastName = "S" + submissionId,
                BirthYear = 1985,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc
            });
            db.Registrations.Add(new Registration
            {
                Id = regBaseId + 500 + i + 1,
                SubmissionId = submissionId,
                PersonId = pid,
                AttendeeType = AttendeeType.Adult,
                Status = RegistrationStatus.Active,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc
            });
        }
        await db.SaveChangesAsync();
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ApplicationDbContext(options));
    }

    /// <summary>
    /// Captures calls; never touches the network. When <see cref="FailForRecipient"/>
    /// matches, throws so we can exercise the bulk error-tolerance path.
    /// </summary>
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
}
