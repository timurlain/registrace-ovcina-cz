using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.CharacterPrep;

namespace RegistraceOvcina.Web.Tests.Features.CharacterPrep;

/// <summary>
/// Route-level coverage for the dashboard page. We don't have a WAF, so these exercise
/// the exact service calls the page performs: GetDashboardStatsAsync + (in the
/// "reflects current state" test) the save → re-read loop the page will drive.
/// </summary>
public sealed class CharacterPrepDashboardStatsRouteTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset NowUtc = new(FixedUtc);
    private const int GameId = 1;
    private const int EquipmentId = 10;

    [Fact]
    public async Task Dashboard_returns_stats_for_game()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1,
            invitedAtUtc: NowUtc,
            players: new[]
            {
                new PlayerSpec(hasName: true, hasEquipment: true),
                new PlayerSpec(hasName: false, hasEquipment: false),
            });
        await AddSubmissionAsync(options, submissionId: 2,
            invitedAtUtc: null,
            players: new[] { new PlayerSpec(hasName: false, hasEquipment: false) });

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var stats = await service.GetDashboardStatsAsync(GameId, CancellationToken.None);

        Assert.Equal(2, stats.TotalHouseholds);
        Assert.Equal(1, stats.Invited);
        // Submission 1 has one player without equipment, so not FullyFilled.
        Assert.Equal(0, stats.FullyFilled);
        // Pending = TotalHouseholds - FullyFilled (per service).
        Assert.Equal(2, stats.Pending);
    }

    [Fact]
    public async Task Stats_reflect_current_state_after_save()
    {
        var options = CreateOptions();
        await SeedGameAsync(options);
        await AddSubmissionAsync(options, submissionId: 1,
            invitedAtUtc: NowUtc,
            players: new[] { new PlayerSpec(hasName: false, hasEquipment: false) });

        var service = new CharacterPrepService(new TestDbContextFactory(options));

        var before = await service.GetDashboardStatsAsync(GameId, CancellationToken.None);
        Assert.Equal(1, before.Invited);
        Assert.Equal(0, before.FullyFilled);
        Assert.Equal(1, before.Pending);

        // Get the single registration id and save name+equipment via the real service,
        // exactly mirroring the organizer-override flow the dashboard is going to use.
        int regId;
        await using (var db = new ApplicationDbContext(options))
        {
            regId = await db.Registrations.Select(x => x.Id).SingleAsync();
        }
        await service.SaveAsync(
            submissionId: 1,
            new[] { new CharacterPrepSaveRow(regId, "Legolas", EquipmentId, null) },
            NowUtc,
            CancellationToken.None);

        var after = await service.GetDashboardStatsAsync(GameId, CancellationToken.None);
        Assert.Equal(1, after.Invited);
        Assert.Equal(1, after.FullyFilled);
        Assert.Equal(0, after.Pending);
    }

    // ------------------------------------------------------------ helpers (mirror of existing tests)

    private sealed record PlayerSpec(bool hasName, bool hasEquipment);

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
            Id = EquipmentId, GameId = GameId, Key = "sword", DisplayName = "Meč", SortOrder = 1
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddSubmissionAsync(
        DbContextOptions<ApplicationDbContext> options,
        int submissionId,
        DateTimeOffset? invitedAtUtc,
        IReadOnlyList<PlayerSpec> players)
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
            PrimaryContactName = "Household " + submissionId,
            PrimaryEmail = $"u{submissionId}@example.cz",
            PrimaryPhone = "777000000",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200,
            CharacterPrepInvitedAtUtc = invitedAtUtc
        });

        for (var i = 0; i < players.Count; i++)
        {
            var spec = players[i];
            var personId = submissionId * 100 + i + 1;
            var regId = submissionId * 100 + i + 1;
            db.People.Add(new Person
            {
                Id = personId, FirstName = "Kid" + i, LastName = "S" + submissionId, BirthYear = 2015,
                CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
            });
            db.Registrations.Add(new Registration
            {
                Id = regId,
                SubmissionId = submissionId,
                PersonId = personId,
                AttendeeType = AttendeeType.Player,
                Status = RegistrationStatus.Active,
                CharacterName = spec.hasName ? "Aragorn" + i : null,
                StartingEquipmentOptionId = spec.hasEquipment ? EquipmentId : null,
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
