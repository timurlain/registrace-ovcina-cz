using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Feedback;

namespace RegistraceOvcina.Web.Tests.Features.Feedback;

/// <summary>
/// Covers <see cref="FeedbackService.GetSubmissionAttendeesAsync"/> — the
/// listing that powers the household submission detail card. Mirrors the
/// in-memory + private TestDbContextFactory pattern used by
/// <c>FeedbackDashboardTests</c>; "now" is injected explicitly via the method
/// argument so window-edge cases are easy to assert.
/// </summary>
public sealed class FeedbackSubmissionAttendeesTests
{
    // The fixed game window used by the seed helper:
    //   StartsAtUtc = FixedUtc + 30 d
    //   EndsAtUtc   = FixedUtc + 32 d
    // Default (no Game.FeedbackOpensAtUtc / FeedbackClosesAtUtc) means the
    // window is [EndsAtUtc, EndsAtUtc + 30 d].
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>"Now" inside the open window — one day after the game ended.</summary>
    private static readonly DateTimeOffset NowUtcInWindow = new(FixedUtc.AddDays(33), TimeSpan.Zero);

    /// <summary>"Now" before the window opens — game has not started yet.</summary>
    private static readonly DateTimeOffset NowUtcBeforeWindow = new(FixedUtc.AddDays(10), TimeSpan.Zero);

    /// <summary>"Now" after the window closes — 31 days past the close.</summary>
    private static readonly DateTimeOffset NowUtcAfterWindow = new(FixedUtc.AddDays(63), TimeSpan.Zero);

    private const string KidJson = """[{"key":"co_libilo","label":"Co se ti líbilo?"}]""";
    private const string AdultJson = """[{"key":"organizace","label":"Organizace?"}]""";

    [Fact]
    public async Task ReturnsAttendeeList_WhenWindowOpen_AndDerivesPerRowStatus()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        // Three attendees on the same submission with three different statuses.
        // 1: not invited → NotInvited
        // 2: invited, no response → Waiting
        // 3: submitted response → Done
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            attendeeType: AttendeeType.Player,
            firstName: "Anna", lastName: "Aaa",
            invitedAtUtc: null, submittedAtUtc: null);
        await SeedRegistrationAsync(options, registrationId: 2, submissionId: 1, personId: 2,
            attendeeType: AttendeeType.Adult,
            firstName: "Bob", lastName: "Bbb",
            invitedAtUtc: NowUtcInWindow.AddDays(-2), submittedAtUtc: null);
        await SeedRegistrationAsync(options, registrationId: 3, submissionId: 1, personId: 3,
            attendeeType: AttendeeType.Player,
            firstName: "Cilka", lastName: "Ccc",
            invitedAtUtc: NowUtcInWindow.AddDays(-3), submittedAtUtc: NowUtcInWindow.AddDays(-1));
        var sut = CreateSut(options);

        var list = await sut.GetSubmissionAttendeesAsync(submissionId: 1, NowUtcInWindow, CancellationToken.None);

        Assert.NotNull(list);
        // Players first (Anna, Cilka), then adults (Bob). Within each, by last name.
        var statuses = list!.ToDictionary(a => a.RegistrationId, a => a.Status);
        Assert.Equal(FeedbackStatus.NotInvited, statuses[1]);
        Assert.Equal(FeedbackStatus.Waiting, statuses[2]);
        Assert.Equal(FeedbackStatus.Done, statuses[3]);
        Assert.Equal(3, list.Count);
        Assert.Equal(new[] { 1, 3, 2 }, list.Select(a => a.RegistrationId).ToArray());
    }

    [Fact]
    public async Task ReturnsNull_WhenWindowAlreadyClosed()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1);
        var sut = CreateSut(options);

        var list = await sut.GetSubmissionAttendeesAsync(
            submissionId: 1, NowUtcAfterWindow, CancellationToken.None);

        Assert.Null(list);
    }

    [Fact]
    public async Task ReturnsNull_WhenWindowNotYetOpen()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1);
        var sut = CreateSut(options);

        var list = await sut.GetSubmissionAttendeesAsync(
            submissionId: 1, NowUtcBeforeWindow, CancellationToken.None);

        Assert.Null(list);
    }

    [Fact]
    public async Task ReturnsNull_WhenSubmissionSoftDeleted()
    {
        var options = CreateOptions();
        // Seed the game's default submission, then mark it deleted. The global
        // query filter on RegistrationSubmission must hide it from the lookup.
        await SeedGameAsync(options, gameId: 1, seedDefaultSubmission: false);
        await SeedSubmissionAsync(options, submissionId: 1, gameId: 1, isDeleted: true);
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1);
        var sut = CreateSut(options);

        var list = await sut.GetSubmissionAttendeesAsync(
            submissionId: 1, NowUtcInWindow, CancellationToken.None);

        Assert.Null(list);
    }

    [Fact]
    public async Task ReturnsNull_WhenSubmissionDoesNotExist()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        var sut = CreateSut(options);

        var list = await sut.GetSubmissionAttendeesAsync(
            submissionId: 999, NowUtcInWindow, CancellationToken.None);

        Assert.Null(list);
    }

    [Fact]
    public async Task ReturnsEmptyList_WhenSubmissionHasNoAttendees()
    {
        // A registrant who created the submission but added no attendees yet
        // sees the card auto-hide via the host's "Count > 0" guard. The service
        // returns an empty (not null) list because the window is open.
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        var sut = CreateSut(options);

        var list = await sut.GetSubmissionAttendeesAsync(
            submissionId: 1, NowUtcInWindow, CancellationToken.None);

        Assert.NotNull(list);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task IncludesBothPlayerAndAdultAttendees_OrderedPlayersFirst()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        // Order on insert is Adult first, Player second — output must reverse it.
        await SeedRegistrationAsync(options, registrationId: 1, submissionId: 1, personId: 1,
            attendeeType: AttendeeType.Adult,
            firstName: "Alfa", lastName: "Adult");
        await SeedRegistrationAsync(options, registrationId: 2, submissionId: 1, personId: 2,
            attendeeType: AttendeeType.Player,
            firstName: "Petr", lastName: "Player");
        var sut = CreateSut(options);

        var list = await sut.GetSubmissionAttendeesAsync(
            submissionId: 1, NowUtcInWindow, CancellationToken.None);

        Assert.NotNull(list);
        Assert.Equal(2, list!.Count);
        // Players come first regardless of seed order.
        Assert.Equal(AttendeeType.Player, list[0].AttendeeType);
        Assert.Equal("Petr Player", list[0].PersonFullName);
        Assert.Equal(AttendeeType.Adult, list[1].AttendeeType);
        Assert.Equal("Alfa Adult", list[1].PersonFullName);
    }

    // -------------------------------------------------------------------------
    // Helpers — same shape as FeedbackDashboardTests so reviewers can diff them.
    // -------------------------------------------------------------------------

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static FeedbackService CreateSut(DbContextOptions<ApplicationDbContext> options)
    {
        var factory = new TestDbContextFactory(options);
        // Time provider's value is unused by GetSubmissionAttendeesAsync (it
        // takes nowUtc as an argument), but FeedbackService still needs one.
        return new FeedbackService(
            factory,
            new FeedbackOptionsService(factory),
            new FixedTimeProvider(NowUtcInWindow));
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
        bool isDeleted = false)
    {
        await using var db = new ApplicationDbContext(options);
        db.RegistrationSubmissions.Add(new RegistrationSubmission
        {
            Id = submissionId,
            GameId = gameId,
            RegistrantUserId = "user-1",
            PrimaryContactName = $"Household {submissionId}",
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
        DateTimeOffset? submittedAtUtc = null)
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
