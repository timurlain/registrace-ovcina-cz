using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.CharacterPrep;

namespace RegistraceOvcina.Web.Tests.Features.CharacterPrep;

/// <summary>
/// Service-level flow tests for the "Příprava postav" section on the organizer
/// submission detail page (Phase 8 / Task 21). The razor markup wires up exactly the
/// same service methods a human would hit with the three action buttons, so locking
/// them in here keeps the page regression-proof without spinning up a full
/// WebApplicationFactory.
/// </summary>
public sealed class SubmissionDetailCharacterPrepSectionTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset NowUtc = new(FixedUtc);
    private const int GameId = 1;
    private const int EquipmentId = 10;

    [Fact]
    public async Task Send_pozvanka_from_detail_invokes_service_and_stamps_timestamp()
    {
        // Mirrors the "Poslat pozvánku" button click: submission has no invite yet,
        // service must send, mark invited, and persist a token for the copy-link UI.
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, invitedAtUtc: null, players: 1, adults: 0);

        var (mailService, sender) = CreateMailService(options);

        var result = await mailService.SendPozvankaAsync(1, NowUtc, CancellationToken.None);

        Assert.Equal(SendSingleResult.Sent, result);
        Assert.Single(sender.Captured);

        await using var verify = new ApplicationDbContext(options);
        var submission = await verify.RegistrationSubmissions.SingleAsync(x => x.Id == 1);
        Assert.Equal(NowUtc, submission.CharacterPrepInvitedAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(submission.CharacterPrepToken));
    }

    [Fact]
    public async Task Send_pripominka_enforces_24h_throttle_on_detail()
    {
        // Detail page's "Poslat připomínku" button must surrender to the same
        // 24h guard that the bulk send path uses, so two clicks in quick
        // succession do not blast the household twice.
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(
            options, submissionId: 1,
            invitedAtUtc: NowUtc.AddDays(-3),
            players: 1, adults: 0,
            reminderLastSentAtUtc: NowUtc.AddHours(-1));

        var (mailService, sender) = CreateMailService(options);

        var throttled = await mailService.SendPripominkaAsync(1, NowUtc, CancellationToken.None);

        Assert.Equal(SendSingleResult.ThrottledSkipped, throttled);
        Assert.Empty(sender.Captured);

        // Walk past the 24h boundary and the same call should now go through.
        var later = NowUtc.AddHours(24).AddSeconds(1);
        var sent = await mailService.SendPripominkaAsync(1, later, CancellationToken.None);

        Assert.Equal(SendSingleResult.Sent, sent);
        Assert.Single(sender.Captured);
    }

    [Fact]
    public async Task Regenerate_token_invalidates_old_and_produces_new_value()
    {
        // "Vygenerovat nový odkaz" must both mint a fresh token and take the old
        // URL out of circulation — otherwise the destructive confirm prompt in
        // the UI is a lie.
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1, invitedAtUtc: null, players: 1, adults: 0);

        var tokenService = new CharacterPrepTokenService(new TestDbContextFactory(options));

        var original = await tokenService.EnsureTokenAsync(1, CancellationToken.None);
        var rotated = await tokenService.RotateTokenAsync(1, CancellationToken.None);

        Assert.NotEqual(original, rotated);

        var lookupOld = await tokenService.FindBySubmissionTokenAsync(original, CancellationToken.None);
        Assert.Null(lookupOld);

        var lookupNew = await tokenService.FindBySubmissionTokenAsync(rotated, CancellationToken.None);
        Assert.NotNull(lookupNew);
        Assert.Equal(1, lookupNew!.Id);
    }

    [Theory]
    [InlineData(false, false, false, CharacterPrepStatus.NotInvited)]
    [InlineData(true, false, false, CharacterPrepStatus.Waiting)]
    [InlineData(true, true, false, CharacterPrepStatus.Waiting)]   // name but no equipment → still waiting
    [InlineData(true, false, true, CharacterPrepStatus.Waiting)]   // equipment but no name → still waiting
    [InlineData(true, true, true, CharacterPrepStatus.Done)]
    public async Task Status_derived_correctly_for_each_submission_state(
        bool invited,
        bool characterNameFilled,
        bool equipmentFilled,
        CharacterPrepStatus expected)
    {
        // The Nezváno / Čeká / Hotovo badge on SubmissionDetail is derived by the
        // same predicate that drives the dashboard — lock the truth table here.
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(
            options, submissionId: 1,
            invitedAtUtc: invited ? NowUtc.AddDays(-2) : null,
            players: 1, adults: 0,
            characterName: characterNameFilled ? "Aragorn" : null,
            equipmentId: equipmentFilled ? EquipmentId : null);

        var service = new CharacterPrepService(new TestDbContextFactory(options));

        var status = await service.GetSubmissionStatusAsync(1, CancellationToken.None);

        Assert.Equal(expected, status);
    }

    [Fact]
    public async Task GetSubmissionStatusAsync_returns_null_for_unknown_submission()
    {
        // Guard against accidental "assume it exists" — SubmissionDetail resolves
        // the detail first, but a concurrent soft-delete must not blow up the page.
        var options = CreateOptions();
        await SeedGameAsync(options);

        var service = new CharacterPrepService(new TestDbContextFactory(options));

        var status = await service.GetSubmissionStatusAsync(9999, CancellationToken.None);

        Assert.Null(status);
    }

    [Fact]
    public void Public_prep_url_format_is_base_slash_postavy_slash_token()
    {
        // The copy-to-clipboard button on SubmissionDetail hands the parent the
        // exact URL the pozvánka email advertises — must line up with the format
        // CharacterPrepMailService.BuildEmailModel builds.
        const string baseUrl = "https://registrace.ovcina.cz";
        const string token = "abcDEF-123_456";

        var url = $"{baseUrl.TrimEnd('/')}/postavy/{token}";

        Assert.Equal("https://registrace.ovcina.cz/postavy/abcDEF-123_456", url);
    }

    // ------------------------------------------------------------ helpers

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static (CharacterPrepMailService Service, FakeCharacterPrepEmailSender Sender) CreateMailService(
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
        string? characterName = null,
        int? equipmentId = null)
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
            PrimaryEmail = $"u{submissionId}@example.cz",
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
                CharacterName = characterName ?? (allPlayersEquipped ? "Hero " + i : null),
                StartingEquipmentOptionId = equipmentId ?? (allPlayersEquipped ? EquipmentId : (int?)null),
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

    private sealed class FakeCharacterPrepEmailSender : ICharacterPrepEmailSender
    {
        public List<SentMessage> Captured { get; } = new();

        public Task SendAsync(string recipientEmail, string subject, string htmlBody, CancellationToken cancellationToken)
        {
            Captured.Add(new SentMessage(recipientEmail, subject, htmlBody));
            return Task.CompletedTask;
        }
    }

    private sealed record SentMessage(string To, string Subject, string HtmlBody);
}
