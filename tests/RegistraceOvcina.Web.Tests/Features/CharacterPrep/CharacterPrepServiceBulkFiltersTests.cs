using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.CharacterPrep;

namespace RegistraceOvcina.Web.Tests.Features.CharacterPrep;

public sealed class CharacterPrepServiceBulkFiltersTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset NowUtc = new(FixedUtc);
    private const int GameId = 1;
    private const int EquipmentId = 10;

    [Fact]
    public async Task Invitation_targets_include_uninvited_submissions_with_Player()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);

        // S1: uninvited, 1 Player  → included
        // S2: invited, 1 Player    → excluded (already invited)
        await AddSubmissionAsync(options, submissionId: 1, invitedAtUtc: null, players: 1, adults: 0);
        await AddSubmissionAsync(options, submissionId: 2, invitedAtUtc: NowUtc.AddDays(-5), players: 1, adults: 0);

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var targets = await service.ListInvitationTargetsAsync(GameId, CancellationToken.None);

        Assert.Single(targets);
        Assert.Equal(1, targets[0].Id);
    }

    [Fact]
    public async Task Invitation_targets_exclude_submissions_with_no_Players()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);

        await AddSubmissionAsync(options, submissionId: 1, invitedAtUtc: null, players: 0, adults: 2);

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var targets = await service.ListInvitationTargetsAsync(GameId, CancellationToken.None);

        Assert.Empty(targets);
    }

    [Fact]
    public async Task Reminder_targets_include_invited_with_empty_equipment_Player_row()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);

        // Invited 3 days ago, 1 Player without equipment  → included
        await AddSubmissionAsync(
            options, submissionId: 1,
            invitedAtUtc: NowUtc.AddDays(-3),
            players: 1, adults: 0);

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var targets = await service.ListReminderTargetsAsync(GameId, NowUtc, CancellationToken.None);

        Assert.Single(targets);
        Assert.Equal(1, targets[0].Id);
    }

    [Fact]
    public async Task Reminder_targets_exclude_fully_filled_submissions()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);

        // Submission invited, both Players have equipment → excluded
        await AddSubmissionAsync(
            options, submissionId: 1,
            invitedAtUtc: NowUtc.AddDays(-3),
            players: 2, adults: 0,
            allPlayersEquipped: true);

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var targets = await service.ListReminderTargetsAsync(GameId, NowUtc, CancellationToken.None);

        Assert.Empty(targets);
    }

    [Fact]
    public async Task Reminder_throttle_excludes_within_last_24h()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);

        await AddSubmissionAsync(
            options, submissionId: 1,
            invitedAtUtc: NowUtc.AddDays(-5),
            players: 1, adults: 0,
            reminderLastSentAtUtc: NowUtc.AddHours(-2));

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var targets = await service.ListReminderTargetsAsync(GameId, NowUtc, CancellationToken.None);

        Assert.Empty(targets);
    }

    [Fact]
    public async Task Reminder_throttle_boundary_includes_slightly_past_24h()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);

        await AddSubmissionAsync(
            options, submissionId: 1,
            invitedAtUtc: NowUtc.AddDays(-5),
            players: 1, adults: 0,
            reminderLastSentAtUtc: NowUtc.AddHours(-24).AddSeconds(-1));

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var targets = await service.ListReminderTargetsAsync(GameId, NowUtc, CancellationToken.None);

        Assert.Single(targets);
    }

    [Fact]
    public async Task MarkInvitedAsync_is_idempotent()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        var originalInvited = NowUtc.AddDays(-3);
        await AddSubmissionAsync(
            options, submissionId: 1,
            invitedAtUtc: originalInvited,
            players: 1, adults: 0);

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        await service.MarkInvitedAsync(1, NowUtc, CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var submission = await verify.RegistrationSubmissions.SingleAsync(x => x.Id == 1);
        Assert.Equal(originalInvited, submission.CharacterPrepInvitedAtUtc);
    }

    [Fact]
    public async Task MarkReminderSentAsync_overwrites_existing_timestamp()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(
            options, submissionId: 1,
            invitedAtUtc: NowUtc.AddDays(-3),
            players: 1, adults: 0,
            reminderLastSentAtUtc: NowUtc.AddDays(-2));

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        await service.MarkReminderSentAsync(1, NowUtc, CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var submission = await verify.RegistrationSubmissions.SingleAsync(x => x.Id == 1);
        Assert.Equal(NowUtc, submission.CharacterPrepReminderLastSentAtUtc);
    }

    [Fact]
    public async Task Dashboard_stats_count_correctly_across_mixed_statuses()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);

        // S1: uninvited, 1 Player without equipment           → Pending, not Invited, not Filled
        // S2: invited, 1 Player without equipment             → Pending, Invited
        // S3: invited, 1 Player fully equipped + named        → Invited, Filled
        // S4: 0 Player (2 Adults only)                        → excluded entirely
        await AddSubmissionAsync(options, submissionId: 1, invitedAtUtc: null, players: 1, adults: 0);
        await AddSubmissionAsync(options, submissionId: 2, invitedAtUtc: NowUtc, players: 1, adults: 0);
        await AddSubmissionAsync(options, submissionId: 3, invitedAtUtc: NowUtc, players: 1, adults: 0, allPlayersEquipped: true);
        await AddSubmissionAsync(options, submissionId: 4, invitedAtUtc: null, players: 0, adults: 2);

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var stats = await service.GetDashboardStatsAsync(GameId, CancellationToken.None);

        Assert.Equal(3, stats.TotalHouseholds);
        Assert.Equal(2, stats.Invited);
        Assert.Equal(1, stats.FullyFilled);
        Assert.Equal(2, stats.Pending);
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

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
            Id = EquipmentId, GameId = GameId, Key = "tesak", DisplayName = "Tesák", SortOrder = 1
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
        DateTimeOffset? reminderLastSentAtUtc = null)
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
        await db.SaveChangesAsync();

        var personBaseId = submissionId * 1000;
        var regBaseId = submissionId * 1000;
        for (var i = 0; i < players; i++)
        {
            var pid = personBaseId + i + 1;
            db.People.Add(new Person
            {
                Id = pid, FirstName = "Player" + i, LastName = "S" + submissionId, BirthYear = 2015,
                CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
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
                Id = pid, FirstName = "Adult" + i, LastName = "S" + submissionId, BirthYear = 1985,
                CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
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
}
