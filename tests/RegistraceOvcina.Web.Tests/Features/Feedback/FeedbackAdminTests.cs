using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Feedback;

namespace RegistraceOvcina.Web.Tests.Features.Feedback;

/// <summary>
/// Covers the organizer-facing admin surface on <see cref="FeedbackService"/>:
/// window override (open / close / extend) and per-response actions
/// (pin to chronicle, organizer note). Mirrors the seeding + DI pattern used
/// by <see cref="FeedbackServiceTests"/> so the two suites stay legible
/// side-by-side.
/// </summary>
public sealed class FeedbackAdminTests
{
    // The fixed game window — same anchor as the sister suite. The default
    // (no Game.FeedbackOpensAtUtc / FeedbackClosesAtUtc) means the window
    // resolves to [EndsAtUtc, EndsAtUtc + 30 d].
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    private const string KidJson = """[{"key":"co_libilo","label":"Co se ti líbilo?"},{"key":"co_zlepsit","label":"Co bys zlepšil?"}]""";
    private const string AdultJson = """[{"key":"organizace","label":"Organizace?"},{"key":"navrh","label":"Návrh?"}]""";

    // -------------------------------------------------------------------------
    // OpenFeedbackAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OpenFeedbackAsync_sets_opens_at()
    {
        var options = CreateOptions();
        await SeedRegistrationAsync(options, AttendeeType.Player);
        var now = NowDuringWindow();
        var sut = CreateSut(options, now);

        await sut.OpenFeedbackAsync(1, now, CancellationToken.None);

        await using var db = new ApplicationDbContext(options);
        var game = await db.Games.AsNoTracking().FirstAsync(x => x.Id == 1);
        Assert.Equal(now, game.FeedbackOpensAtUtc);
    }

    [Fact]
    public async Task OpenFeedbackAsync_bumps_close_when_close_was_past()
    {
        var options = CreateOptions();
        await SeedRegistrationAsync(options, AttendeeType.Player);
        var now = NowDuringWindow();
        // Set FeedbackClosesAtUtc to a past timestamp before the operation.
        await using (var seed = new ApplicationDbContext(options))
        {
            var game = await seed.Games.FirstAsync(x => x.Id == 1);
            game.FeedbackClosesAtUtc = now.AddDays(-5);
            await seed.SaveChangesAsync();
        }

        var sut = CreateSut(options, now);
        await sut.OpenFeedbackAsync(1, now, CancellationToken.None);

        await using var db = new ApplicationDbContext(options);
        var reopened = await db.Games.AsNoTracking().FirstAsync(x => x.Id == 1);
        Assert.Equal(now, reopened.FeedbackOpensAtUtc);
        Assert.Equal(now.AddDays(30), reopened.FeedbackClosesAtUtc);
    }

    [Fact]
    public async Task OpenFeedbackAsync_leaves_close_when_close_in_future()
    {
        var options = CreateOptions();
        await SeedRegistrationAsync(options, AttendeeType.Player);
        var now = NowDuringWindow();
        var futureClose = now.AddDays(10);
        await using (var seed = new ApplicationDbContext(options))
        {
            var game = await seed.Games.FirstAsync(x => x.Id == 1);
            game.FeedbackClosesAtUtc = futureClose;
            await seed.SaveChangesAsync();
        }

        var sut = CreateSut(options, now);
        await sut.OpenFeedbackAsync(1, now, CancellationToken.None);

        await using var db = new ApplicationDbContext(options);
        var game2 = await db.Games.AsNoTracking().FirstAsync(x => x.Id == 1);
        Assert.Equal(now, game2.FeedbackOpensAtUtc);
        Assert.Equal(futureClose, game2.FeedbackClosesAtUtc);
    }

    [Fact]
    public async Task OpenFeedbackAsync_throws_when_game_missing()
    {
        var options = CreateOptions();
        var sut = CreateSut(options, NowDuringWindow());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.OpenFeedbackAsync(999, NowDuringWindow(), CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    // CloseFeedbackAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CloseFeedbackAsync_sets_closes_at()
    {
        var options = CreateOptions();
        await SeedRegistrationAsync(options, AttendeeType.Player);
        var now = NowDuringWindow();
        var sut = CreateSut(options, now);

        await sut.CloseFeedbackAsync(1, now, CancellationToken.None);

        await using var db = new ApplicationDbContext(options);
        var game = await db.Games.AsNoTracking().FirstAsync(x => x.Id == 1);
        Assert.Equal(now, game.FeedbackClosesAtUtc);
    }

    [Fact]
    public async Task CloseFeedbackAsync_throws_when_game_missing()
    {
        var options = CreateOptions();
        var sut = CreateSut(options, NowDuringWindow());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.CloseFeedbackAsync(999, NowDuringWindow(), CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    // ExtendFeedbackAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExtendFeedbackAsync_adds_30_days_to_now_when_close_in_past()
    {
        var options = CreateOptions();
        await SeedRegistrationAsync(options, AttendeeType.Player);
        var now = NowDuringWindow();
        await using (var seed = new ApplicationDbContext(options))
        {
            var game = await seed.Games.FirstAsync(x => x.Id == 1);
            game.FeedbackClosesAtUtc = now.AddDays(-3);
            await seed.SaveChangesAsync();
        }

        var sut = CreateSut(options, now);
        await sut.ExtendFeedbackAsync(1, now, CancellationToken.None);

        await using var db = new ApplicationDbContext(options);
        var game2 = await db.Games.AsNoTracking().FirstAsync(x => x.Id == 1);
        Assert.Equal(now.AddDays(30), game2.FeedbackClosesAtUtc);
    }

    [Fact]
    public async Task ExtendFeedbackAsync_adds_30_days_to_close_when_close_in_future()
    {
        var options = CreateOptions();
        await SeedRegistrationAsync(options, AttendeeType.Player);
        var now = NowDuringWindow();
        var futureClose = now.AddDays(7);
        await using (var seed = new ApplicationDbContext(options))
        {
            var game = await seed.Games.FirstAsync(x => x.Id == 1);
            game.FeedbackClosesAtUtc = futureClose;
            await seed.SaveChangesAsync();
        }

        var sut = CreateSut(options, now);
        await sut.ExtendFeedbackAsync(1, now, CancellationToken.None);

        await using var db = new ApplicationDbContext(options);
        var game2 = await db.Games.AsNoTracking().FirstAsync(x => x.Id == 1);
        Assert.Equal(futureClose.AddDays(30), game2.FeedbackClosesAtUtc);
    }

    [Fact]
    public async Task ExtendFeedbackAsync_throws_when_game_missing()
    {
        var options = CreateOptions();
        var sut = CreateSut(options, NowDuringWindow());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ExtendFeedbackAsync(999, NowDuringWindow(), CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    // SetPinToChronicleAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetPinToChronicleAsync_sets_flag()
    {
        var options = CreateOptions();
        await SeedRegistrationAsync(options, AttendeeType.Player);
        await SeedExistingResponseAsync(options, 1, """{"co_libilo":"Bitva"}""");
        var sut = CreateSut(options, NowDuringWindow());

        await sut.SetPinToChronicleAsync(1, true, CancellationToken.None);

        await using var db = new ApplicationDbContext(options);
        var response = await db.FeedbackResponses.AsNoTracking().FirstAsync(x => x.RegistrationId == 1);
        Assert.True(response.PinToChronicle);
    }

    [Fact]
    public async Task SetPinToChronicleAsync_unsets_flag()
    {
        var options = CreateOptions();
        await SeedRegistrationAsync(options, AttendeeType.Player);
        await SeedExistingResponseAsync(options, 1, """{"co_libilo":"Bitva"}""", pinned: true);
        var sut = CreateSut(options, NowDuringWindow());

        await sut.SetPinToChronicleAsync(1, false, CancellationToken.None);

        await using var db = new ApplicationDbContext(options);
        var response = await db.FeedbackResponses.AsNoTracking().FirstAsync(x => x.RegistrationId == 1);
        Assert.False(response.PinToChronicle);
    }

    [Fact]
    public async Task SetPinToChronicleAsync_throws_when_no_response_exists()
    {
        var options = CreateOptions();
        await SeedRegistrationAsync(options, AttendeeType.Player);
        var sut = CreateSut(options, NowDuringWindow());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.SetPinToChronicleAsync(1, true, CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    // SaveOrganizerNoteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaveOrganizerNoteAsync_persists_note()
    {
        var options = CreateOptions();
        await SeedRegistrationAsync(options, AttendeeType.Player);
        await SeedExistingResponseAsync(options, 1, """{"co_libilo":"Bitva"}""");
        var sut = CreateSut(options, NowDuringWindow());

        await sut.SaveOrganizerNoteAsync(1, "Citace pro kroniku.", CancellationToken.None);

        await using var db = new ApplicationDbContext(options);
        var response = await db.FeedbackResponses.AsNoTracking().FirstAsync(x => x.RegistrationId == 1);
        Assert.Equal("Citace pro kroniku.", response.OrganizerNote);
    }

    [Fact]
    public async Task SaveOrganizerNoteAsync_clears_on_blank()
    {
        var options = CreateOptions();
        await SeedRegistrationAsync(options, AttendeeType.Player);
        await SeedExistingResponseAsync(options, 1, """{"co_libilo":"Bitva"}""", note: "Stará poznámka");
        var sut = CreateSut(options, NowDuringWindow());

        await sut.SaveOrganizerNoteAsync(1, "   ", CancellationToken.None);

        await using var db = new ApplicationDbContext(options);
        var response = await db.FeedbackResponses.AsNoTracking().FirstAsync(x => x.RegistrationId == 1);
        Assert.Null(response.OrganizerNote);
    }

    [Fact]
    public async Task SaveOrganizerNoteAsync_throws_when_no_response_exists()
    {
        var options = CreateOptions();
        await SeedRegistrationAsync(options, AttendeeType.Player);
        var sut = CreateSut(options, NowDuringWindow());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.SaveOrganizerNoteAsync(1, "Bez šance.", CancellationToken.None));
    }

    // -------------------------------------------------------------------------
    // helpers
    // -------------------------------------------------------------------------

    private static DateTimeOffset NowDuringWindow() =>
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

    private static async Task SeedRegistrationAsync(
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
    }

    private static async Task SeedExistingResponseAsync(
        DbContextOptions<ApplicationDbContext> options,
        int registrationId,
        string answersJson,
        bool pinned = false,
        string? note = null)
    {
        await using var db = new ApplicationDbContext(options);
        db.FeedbackResponses.Add(new FeedbackResponse
        {
            RegistrationId = registrationId,
            AnswersJson = answersJson,
            LastEditedBy = FeedbackEditedBy.Self,
            CreatedAtUtc = new DateTimeOffset(FixedUtc.AddDays(33), TimeSpan.Zero),
            UpdatedAtUtc = new DateTimeOffset(FixedUtc.AddDays(33), TimeSpan.Zero),
            PinToChronicle = pinned,
            OrganizerNote = note,
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
}
