using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Feedback;

namespace RegistraceOvcina.Web.Tests.Features.Feedback;

/// <summary>
/// Covers the organizer dashboard surface area on <see cref="FeedbackService"/>:
/// stats counts, paginated/filterable per-attendee rows, invitation/reminder
/// targets, and the mark-invited / mark-reminder-sent operations. Mirrors the
/// in-memory + private TestDbContextFactory pattern used by
/// <c>FeedbackServiceTests</c>.
/// </summary>
public sealed class FeedbackDashboardTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset NowUtc = new(FixedUtc.AddDays(40), TimeSpan.Zero);

    private const string KidJson = """[{"key":"co_libilo","label":"Co se ti líbilo?"}]""";
    private const string AdultJson = """[{"key":"organizace","label":"Organizace?"}]""";

    // -------------------------------------------------------------------------
    // GetDashboardStatsAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDashboardStatsAsync_returns_zeros_for_game_with_no_registrations()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        var sut = CreateSut(options);

        var stats = await sut.GetDashboardStatsAsync(gameId: 1, CancellationToken.None);

        Assert.Equal(new FeedbackStats(0, 0, 0, 0), stats);
    }

    [Fact]
    public async Task GetDashboardStatsAsync_counts_total_invited_done_waiting_correctly()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        // 5 registrations:
        //   1 not invited
        //   2 invited, not submitted (waiting)
        //   2 invited, submitted (done)
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            invitedAtUtc: null, submittedAtUtc: null);
        await SeedRegistrationAsync(options, registrationId: 2, submissionId: 1, personId: 2,
            invitedAtUtc: NowUtc.AddDays(-2), submittedAtUtc: null);
        await SeedRegistrationAsync(options, registrationId: 3, submissionId: 1, personId: 3,
            invitedAtUtc: NowUtc.AddDays(-2), submittedAtUtc: null);
        await SeedRegistrationAsync(options, registrationId: 4, submissionId: 1, personId: 4,
            invitedAtUtc: NowUtc.AddDays(-3), submittedAtUtc: NowUtc.AddDays(-1));
        await SeedRegistrationAsync(options, registrationId: 5, submissionId: 1, personId: 5,
            invitedAtUtc: NowUtc.AddDays(-3), submittedAtUtc: NowUtc.AddDays(-1));
        var sut = CreateSut(options);

        var stats = await sut.GetDashboardStatsAsync(gameId: 1, CancellationToken.None);

        Assert.Equal(5, stats.Total);
        Assert.Equal(4, stats.Invited);
        Assert.Equal(2, stats.Done);
        Assert.Equal(2, stats.Waiting);
    }

    [Fact]
    public async Task GetDashboardStatsAsync_excludes_soft_deleted_submissions()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedSubmissionAsync(options, submissionId: 2, gameId: 1, isDeleted: true);
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            invitedAtUtc: NowUtc.AddDays(-2), submittedAtUtc: null);
        await SeedRegistrationAsync(options, registrationId: 2, submissionId: 2, personId: 2,
            invitedAtUtc: NowUtc.AddDays(-2), submittedAtUtc: NowUtc.AddDays(-1));
        var sut = CreateSut(options);

        var stats = await sut.GetDashboardStatsAsync(gameId: 1, CancellationToken.None);

        Assert.Equal(1, stats.Total);
        Assert.Equal(1, stats.Invited);
        Assert.Equal(0, stats.Done);
        Assert.Equal(1, stats.Waiting);
    }

    [Fact]
    public async Task GetDashboardStatsAsync_excludes_other_games()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedGameAsync(options, gameId: 2, seedDefaultSubmission: false);
        await SeedSubmissionAsync(options, submissionId: 100, gameId: 2);
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            invitedAtUtc: NowUtc.AddDays(-2), submittedAtUtc: null);
        await SeedRegistrationAsync(options, registrationId: 2, submissionId: 100, personId: 2,
            invitedAtUtc: NowUtc.AddDays(-2), submittedAtUtc: NowUtc.AddDays(-1));
        var sut = CreateSut(options);

        var stats = await sut.GetDashboardStatsAsync(gameId: 1, CancellationToken.None);

        Assert.Equal(1, stats.Total);
    }

    // -------------------------------------------------------------------------
    // GetDashboardRowsAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDashboardRowsAsync_returns_empty_for_game_with_no_registrations()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        var sut = CreateSut(options);

        var page = await sut.GetDashboardRowsAsync(
            gameId: 1, new FeedbackDashboardFilter(), page: 1, pageSize: 20, CancellationToken.None);

        Assert.Empty(page.Rows);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task GetDashboardRowsAsync_paginates()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1, seedDefaultSubmission: false);
        // Distinct households so the secondary sort stays stable.
        for (var i = 1; i <= 5; i++)
        {
            await SeedSubmissionAsync(options, submissionId: i, gameId: 1, primaryContactName: $"House {i:00}");
            await SeedRegistrationAsync(options, registrationId: i, submissionId: i, personId: i,
                invitedAtUtc: null, submittedAtUtc: null);
        }
        var sut = CreateSut(options);

        var page1 = await sut.GetDashboardRowsAsync(
            gameId: 1, new FeedbackDashboardFilter(), page: 1, pageSize: 2, CancellationToken.None);
        var page2 = await sut.GetDashboardRowsAsync(
            gameId: 1, new FeedbackDashboardFilter(), page: 2, pageSize: 2, CancellationToken.None);
        var page3 = await sut.GetDashboardRowsAsync(
            gameId: 1, new FeedbackDashboardFilter(), page: 3, pageSize: 2, CancellationToken.None);

        Assert.Equal(5, page1.Total);
        Assert.Equal(2, page1.Rows.Count);
        Assert.Equal(2, page2.Rows.Count);
        Assert.Single(page3.Rows);
        // Pages don't overlap.
        var allIds = page1.Rows.Concat(page2.Rows).Concat(page3.Rows).Select(r => r.RegistrationId).ToList();
        Assert.Equal(5, allIds.Distinct().Count());
    }

    [Fact]
    public async Task GetDashboardRowsAsync_filters_by_status()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            invitedAtUtc: null, submittedAtUtc: null); // NotInvited
        await SeedRegistrationAsync(options, registrationId: 2, submissionId: 1, personId: 2,
            invitedAtUtc: NowUtc.AddDays(-2), submittedAtUtc: null); // Waiting
        await SeedRegistrationAsync(options, registrationId: 3, submissionId: 1, personId: 3,
            invitedAtUtc: NowUtc.AddDays(-2), submittedAtUtc: NowUtc.AddDays(-1)); // Done
        var sut = CreateSut(options);

        var notInvited = await sut.GetDashboardRowsAsync(
            gameId: 1, new FeedbackDashboardFilter(Status: FeedbackStatus.NotInvited),
            page: 1, pageSize: 20, CancellationToken.None);
        var waiting = await sut.GetDashboardRowsAsync(
            gameId: 1, new FeedbackDashboardFilter(Status: FeedbackStatus.Waiting),
            page: 1, pageSize: 20, CancellationToken.None);
        var done = await sut.GetDashboardRowsAsync(
            gameId: 1, new FeedbackDashboardFilter(Status: FeedbackStatus.Done),
            page: 1, pageSize: 20, CancellationToken.None);

        Assert.Equal(1, Assert.Single(notInvited.Rows).RegistrationId);
        Assert.Equal(2, Assert.Single(waiting.Rows).RegistrationId);
        Assert.Equal(3, Assert.Single(done.Rows).RegistrationId);
    }

    [Fact]
    public async Task GetDashboardRowsAsync_filters_by_attendee_type()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            attendeeType: AttendeeType.Player);
        await SeedRegistrationAsync(options, registrationId: 2, submissionId: 1, personId: 2,
            attendeeType: AttendeeType.Adult);
        var sut = CreateSut(options);

        var players = await sut.GetDashboardRowsAsync(
            gameId: 1, new FeedbackDashboardFilter(AttendeeType: AttendeeType.Player),
            page: 1, pageSize: 20, CancellationToken.None);
        var adults = await sut.GetDashboardRowsAsync(
            gameId: 1, new FeedbackDashboardFilter(AttendeeType: AttendeeType.Adult),
            page: 1, pageSize: 20, CancellationToken.None);

        Assert.Equal(1, Assert.Single(players.Rows).RegistrationId);
        Assert.Equal(2, Assert.Single(adults.Rows).RegistrationId);
    }

    [Fact]
    public async Task GetDashboardRowsAsync_filters_by_search_term_on_person_or_household()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedSubmissionAsync(options, submissionId: 2, gameId: 1, primaryContactName: "Novákovi");
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            firstName: "Karel", lastName: "Svoboda");
        await SeedRegistrationAsync(options, registrationId: 2, submissionId: 2, personId: 2,
            firstName: "Petr", lastName: "Černý");
        await SeedRegistrationAsync(options, registrationId: 3, submissionId: 1, personId: 3,
            firstName: "Anna", lastName: "Dvořáková");
        var sut = CreateSut(options);

        // Match on person last name.
        var bySvoboda = await sut.GetDashboardRowsAsync(
            gameId: 1, new FeedbackDashboardFilter(Search: "svoboda"),
            page: 1, pageSize: 20, CancellationToken.None);
        // Match on household name.
        var byHousehold = await sut.GetDashboardRowsAsync(
            gameId: 1, new FeedbackDashboardFilter(Search: "novák"),
            page: 1, pageSize: 20, CancellationToken.None);

        Assert.Equal(1, Assert.Single(bySvoboda.Rows).RegistrationId);
        Assert.Equal(2, Assert.Single(byHousehold.Rows).RegistrationId);
    }

    [Fact]
    public async Task GetDashboardRowsAsync_sorts_by_status_then_household_then_person()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedSubmissionAsync(options, submissionId: 2, gameId: 1, primaryContactName: "Bbb household");
        await SeedSubmissionAsync(options, submissionId: 3, gameId: 1, primaryContactName: "Aaa household");

        // Submitted (Done) — should be last.
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            firstName: "Zoe", lastName: "Done",
            invitedAtUtc: NowUtc.AddDays(-2), submittedAtUtc: NowUtc.AddDays(-1));
        // NotInvited (status 0) on Bbb household, two people.
        await SeedRegistrationAsync(options, registrationId: 2, submissionId: 2, personId: 2,
            firstName: "Anna", lastName: "Bbb");
        await SeedRegistrationAsync(options, registrationId: 3, submissionId: 2, personId: 3,
            firstName: "Karel", lastName: "Bbb");
        // NotInvited on Aaa household — should sort first overall.
        await SeedRegistrationAsync(options, registrationId: 4, submissionId: 3, personId: 4,
            firstName: "Marek", lastName: "Aaa");
        var sut = CreateSut(options);

        var page = await sut.GetDashboardRowsAsync(
            gameId: 1, new FeedbackDashboardFilter(), page: 1, pageSize: 20, CancellationToken.None);

        var ids = page.Rows.Select(r => r.RegistrationId).ToList();
        // Status asc: NotInvited (4, 2, 3) then Done (1). Within NotInvited:
        // household "Aaa household" before "Bbb household". Within Bbb, person
        // "Anna Bbb" before "Karel Bbb".
        Assert.Equal(new[] { 4, 2, 3, 1 }, ids);
    }

    [Fact]
    public async Task GetDashboardRowsAsync_excludes_soft_deleted_submissions()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedSubmissionAsync(options, submissionId: 2, gameId: 1, isDeleted: true);
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1);
        await SeedRegistrationAsync(options, registrationId: 2, submissionId: 2, personId: 2);
        var sut = CreateSut(options);

        var page = await sut.GetDashboardRowsAsync(
            gameId: 1, new FeedbackDashboardFilter(), page: 1, pageSize: 20, CancellationToken.None);

        Assert.Equal(1, page.Total);
        Assert.Equal(1, Assert.Single(page.Rows).RegistrationId);
    }

    // -------------------------------------------------------------------------
    // ListInvitationTargetsAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListInvitationTargetsAsync_returns_only_not_invited()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            invitedAtUtc: null);
        await SeedRegistrationAsync(options, registrationId: 2, submissionId: 1, personId: 2,
            invitedAtUtc: NowUtc.AddDays(-2));
        await SeedRegistrationAsync(options, registrationId: 3, submissionId: 1, personId: 3,
            invitedAtUtc: null);
        var sut = CreateSut(options);

        var targets = await sut.ListInvitationTargetsAsync(gameId: 1, CancellationToken.None);

        Assert.Equal(2, targets.Count);
        Assert.Equal(new[] { 1, 3 }, targets.Select(r => r.Id).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task ListInvitationTargetsAsync_excludes_soft_deleted_submissions()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedSubmissionAsync(options, submissionId: 2, gameId: 1, isDeleted: true);
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            invitedAtUtc: null);
        await SeedRegistrationAsync(options, registrationId: 2, submissionId: 2, personId: 2,
            invitedAtUtc: null);
        var sut = CreateSut(options);

        var targets = await sut.ListInvitationTargetsAsync(gameId: 1, CancellationToken.None);

        Assert.Equal(1, Assert.Single(targets).Id);
    }

    [Fact]
    public async Task ListInvitationTargetsAsync_excludes_other_games()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedGameAsync(options, gameId: 2, seedDefaultSubmission: false);
        await SeedSubmissionAsync(options, submissionId: 100, gameId: 2);
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            invitedAtUtc: null);
        await SeedRegistrationAsync(options, registrationId: 2, submissionId: 100, personId: 2,
            invitedAtUtc: null);
        var sut = CreateSut(options);

        var targets = await sut.ListInvitationTargetsAsync(gameId: 1, CancellationToken.None);

        Assert.Equal(1, Assert.Single(targets).Id);
    }

    // -------------------------------------------------------------------------
    // ListReminderTargetsAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListReminderTargetsAsync_returns_invited_not_done_with_no_recent_reminder()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        // Eligible: invited 3 days ago, never reminded, not submitted.
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            invitedAtUtc: NowUtc.AddDays(-3), submittedAtUtc: null,
            reminderLastSentAtUtc: null);
        // Eligible: last reminder 2 days ago.
        await SeedRegistrationAsync(options, registrationId: 2, submissionId: 1, personId: 2,
            invitedAtUtc: NowUtc.AddDays(-5), submittedAtUtc: null,
            reminderLastSentAtUtc: NowUtc.AddDays(-2));
        var sut = CreateSut(options);

        var targets = await sut.ListReminderTargetsAsync(gameId: 1, NowUtc, CancellationToken.None);

        Assert.Equal(2, targets.Count);
        Assert.Equal(new[] { 1, 2 }, targets.Select(r => r.Id).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task ListReminderTargetsAsync_excludes_those_with_reminder_within_24h()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        // Reminded 2h ago — out.
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            invitedAtUtc: NowUtc.AddDays(-3), submittedAtUtc: null,
            reminderLastSentAtUtc: NowUtc.AddHours(-2));
        // Reminded 30h ago — in.
        await SeedRegistrationAsync(options, registrationId: 2, submissionId: 1, personId: 2,
            invitedAtUtc: NowUtc.AddDays(-3), submittedAtUtc: null,
            reminderLastSentAtUtc: NowUtc.AddHours(-30));
        var sut = CreateSut(options);

        var targets = await sut.ListReminderTargetsAsync(gameId: 1, NowUtc, CancellationToken.None);

        Assert.Equal(2, Assert.Single(targets).Id);
    }

    [Fact]
    public async Task ListReminderTargetsAsync_excludes_done()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            invitedAtUtc: NowUtc.AddDays(-3), submittedAtUtc: NowUtc.AddDays(-1));
        await SeedRegistrationAsync(options, registrationId: 2, submissionId: 1, personId: 2,
            invitedAtUtc: NowUtc.AddDays(-3), submittedAtUtc: null);
        var sut = CreateSut(options);

        var targets = await sut.ListReminderTargetsAsync(gameId: 1, NowUtc, CancellationToken.None);

        Assert.Equal(2, Assert.Single(targets).Id);
    }

    [Fact]
    public async Task ListReminderTargetsAsync_excludes_not_invited()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            invitedAtUtc: null);
        await SeedRegistrationAsync(options, registrationId: 2, submissionId: 1, personId: 2,
            invitedAtUtc: NowUtc.AddDays(-3), submittedAtUtc: null);
        var sut = CreateSut(options);

        var targets = await sut.ListReminderTargetsAsync(gameId: 1, NowUtc, CancellationToken.None);

        Assert.Equal(2, Assert.Single(targets).Id);
    }

    // -------------------------------------------------------------------------
    // MarkInvitedAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarkInvitedAsync_sets_FeedbackInvitedAtUtc()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            invitedAtUtc: null);
        var sut = CreateSut(options);

        await sut.MarkInvitedAsync(registrationId: 1, NowUtc, CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var reg = await verify.Registrations.SingleAsync(r => r.Id == 1);
        Assert.Equal(NowUtc, reg.FeedbackInvitedAtUtc);
    }

    [Fact]
    public async Task MarkInvitedAsync_is_idempotent()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        var firstInvite = NowUtc.AddDays(-3);
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            invitedAtUtc: firstInvite);
        var sut = CreateSut(options);

        // Second call must NOT overwrite the original invite timestamp.
        await sut.MarkInvitedAsync(registrationId: 1, NowUtc, CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var reg = await verify.Registrations.SingleAsync(r => r.Id == 1);
        Assert.Equal(firstInvite, reg.FeedbackInvitedAtUtc);
    }

    [Fact]
    public async Task MarkInvitedAsync_throws_when_registration_missing()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        var sut = CreateSut(options);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.MarkInvitedAsync(registrationId: 999, NowUtc, CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    // MarkReminderSentAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MarkReminderSentAsync_sets_FeedbackReminderLastSentAtUtc()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            invitedAtUtc: NowUtc.AddDays(-3), reminderLastSentAtUtc: null);
        var sut = CreateSut(options);

        await sut.MarkReminderSentAsync(registrationId: 1, NowUtc, CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var reg = await verify.Registrations.SingleAsync(r => r.Id == 1);
        Assert.Equal(NowUtc, reg.FeedbackReminderLastSentAtUtc);
    }

    [Fact]
    public async Task MarkReminderSentAsync_overwrites_previous_value()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        var earlier = NowUtc.AddDays(-2);
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            invitedAtUtc: NowUtc.AddDays(-5), reminderLastSentAtUtc: earlier);
        var sut = CreateSut(options);

        await sut.MarkReminderSentAsync(registrationId: 1, NowUtc, CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var reg = await verify.Registrations.SingleAsync(r => r.Id == 1);
        Assert.Equal(NowUtc, reg.FeedbackReminderLastSentAtUtc);
        Assert.NotEqual(earlier, reg.FeedbackReminderLastSentAtUtc);
    }

    [Fact]
    public async Task MarkReminderSentAsync_throws_when_registration_missing()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        var sut = CreateSut(options);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.MarkReminderSentAsync(registrationId: 999, NowUtc, CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    // helpers
    // -------------------------------------------------------------------------

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static FeedbackService CreateSut(DbContextOptions<ApplicationDbContext> options)
    {
        var factory = new TestDbContextFactory(options);
        return new FeedbackService(
            factory,
            new FeedbackOptionsService(factory),
            new FixedTimeProvider(NowUtc));
    }

    private static async Task SeedGameAsync(
        DbContextOptions<ApplicationDbContext> options,
        int gameId,
        bool seedDefaultSubmission = true)
    {
        await using var db = new ApplicationDbContext(options);

        if (!await db.Users.AnyAsync())
        {
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
        }

        db.Games.Add(new Game
        {
            Id = gameId,
            Name = $"Ovčina {2026 + gameId}",
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
        // Default submission (id == gameId) for the most common test path. A
        // game-1 submission has id == 1; game-2 → id == 2. Tests that need
        // additional submissions call SeedSubmissionAsync explicitly. Pass
        // seedDefaultSubmission=false when the test wants to control all
        // submission ids itself (e.g. multi-game tests where the second
        // game's default submission id would collide).
        if (seedDefaultSubmission)
        {
            db.RegistrationSubmissions.Add(new RegistrationSubmission
            {
                Id = gameId,
                GameId = gameId,
                RegistrantUserId = "user-1",
                PrimaryContactName = $"Household {gameId}",
                PrimaryEmail = "parent@example.cz",
                PrimaryPhone = "777111222",
                Status = SubmissionStatus.Submitted,
                SubmittedAtUtc = FixedUtc,
                LastEditedAtUtc = FixedUtc,
                ExpectedTotalAmount = 1200,
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedSubmissionAsync(
        DbContextOptions<ApplicationDbContext> options,
        int submissionId,
        int gameId,
        string? primaryContactName = null,
        bool isDeleted = false)
    {
        await using var db = new ApplicationDbContext(options);
        db.RegistrationSubmissions.Add(new RegistrationSubmission
        {
            Id = submissionId,
            GameId = gameId,
            RegistrantUserId = "user-1",
            PrimaryContactName = primaryContactName ?? $"Household {submissionId}",
            PrimaryEmail = "parent@example.cz",
            PrimaryPhone = "777111222",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200,
            IsDeleted = isDeleted,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedRegistrationAsync(
        DbContextOptions<ApplicationDbContext> options,
        int registrationId,
        int submissionId,
        int personId,
        AttendeeType attendeeType = AttendeeType.Player,
        string? firstName = null,
        string? lastName = null,
        DateTimeOffset? invitedAtUtc = null,
        DateTimeOffset? submittedAtUtc = null,
        DateTimeOffset? reminderLastSentAtUtc = null)
    {
        await using var db = new ApplicationDbContext(options);
        db.People.Add(new Person
        {
            Id = personId,
            FirstName = firstName ?? $"First{personId}",
            LastName = lastName ?? $"Last{personId}",
            BirthYear = attendeeType == AttendeeType.Player ? 2015 : 1985,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc,
        });
        db.Registrations.Add(new Registration
        {
            Id = registrationId,
            SubmissionId = submissionId,
            PersonId = personId,
            AttendeeType = attendeeType,
            Status = RegistrationStatus.Active,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc,
            FeedbackInvitedAtUtc = invitedAtUtc,
            FeedbackReminderLastSentAtUtc = reminderLastSentAtUtc,
        });
        if (submittedAtUtc is not null)
        {
            db.FeedbackResponses.Add(new FeedbackResponse
            {
                RegistrationId = registrationId,
                AnswersJson = "{}",
                LastEditedBy = FeedbackEditedBy.Self,
                CreatedAtUtc = invitedAtUtc ?? new DateTimeOffset(FixedUtc, TimeSpan.Zero),
                UpdatedAtUtc = submittedAtUtc.Value,
                SubmittedAtUtc = submittedAtUtc,
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
}
