using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.CharacterPrep;

namespace RegistraceOvcina.Web.Tests.Features.CharacterPrep;

/// <summary>
/// Tests that cover the data-resolution flow used by the parent-facing
/// <c>/postavy/{token}</c> page (Task 11–14). The page itself is a thin
/// Razor wrapper around the two services exercised here, so verifying the
/// service contracts end-to-end is the cheapest reliable way to lock in
/// the page behaviour without introducing a new test harness (no
/// WebApplicationFactory / bUnit are configured in this test project).
/// </summary>
public sealed class CharacterPrepPageFlowTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset NowUtc = new(FixedUtc);

    // ----- Task 11: token resolution path -----

    [Fact]
    public async Task Unknown_token_resolves_to_null_so_page_renders_not_found()
    {
        var options = CreateOptions();
        await SeedAsync(options, token: "known-token");

        var tokens = new CharacterPrepTokenService(new TestDbContextFactory(options));

        var submission = await tokens.FindBySubmissionTokenAsync("not-the-token", CancellationToken.None);

        Assert.Null(submission);
    }

    [Fact]
    public async Task Valid_token_resolves_submission_and_view_has_player_rows()
    {
        var options = CreateOptions();
        await SeedAsync(options, token: "happy-path-token");

        var tokens = new CharacterPrepTokenService(new TestDbContextFactory(options));
        var service = new CharacterPrepService(new TestDbContextFactory(options));

        var submission = await tokens.FindBySubmissionTokenAsync("happy-path-token", CancellationToken.None);
        Assert.NotNull(submission);

        var view = await service.GetPrepViewAsync(submission!.Id, NowUtc, CancellationToken.None);

        Assert.NotNull(view);
        Assert.Equal("Ovčina 2026", view!.GameName);
        Assert.Single(view.Rows);
        Assert.Equal("Kid One", view.Rows[0].PersonFullName);
        Assert.False(view.IsReadOnly);
    }

    [Fact]
    public async Task Empty_token_resolves_to_null()
    {
        var options = CreateOptions();
        await SeedAsync(options, token: "anything");

        var tokens = new CharacterPrepTokenService(new TestDbContextFactory(options));

        Assert.Null(await tokens.FindBySubmissionTokenAsync("", CancellationToken.None));
        Assert.Null(await tokens.FindBySubmissionTokenAsync("   ", CancellationToken.None));
    }

    // ----- Task 12: card pre-fill (existing values surface through GetPrepViewAsync) -----

    [Fact]
    public async Task Prefill_values_appear_on_row_when_previously_saved()
    {
        var options = CreateOptions();
        await SeedAsync(options, token: "t");

        await using (var db = new ApplicationDbContext(options))
        {
            var reg = await db.Registrations.SingleAsync();
            reg.CharacterName = "Aragorn";
            reg.StartingEquipmentOptionId = 10;
            reg.CharacterPrepNote = "chce luk";
            reg.CharacterPrepUpdatedAtUtc = NowUtc.AddHours(-2);
            await db.SaveChangesAsync();
        }

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var view = await service.GetPrepViewAsync(submissionId: 1, NowUtc, CancellationToken.None);

        Assert.NotNull(view);
        var row = Assert.Single(view!.Rows);
        Assert.Equal("Aragorn", row.CharacterName);
        Assert.Equal(10, row.StartingEquipmentOptionId);
        Assert.Equal("chce luk", row.CharacterPrepNote);
        Assert.NotNull(row.UpdatedAtUtc);
    }

    // ----- Task 13: save round-trip -----

    [Fact]
    public async Task Save_roundtrip_updates_registration_and_view_reflects_changes()
    {
        var options = CreateOptions();
        await SeedAsync(options, token: "t");

        var service = new CharacterPrepService(new TestDbContextFactory(options));

        // Act — simulate the page's save handler projecting editable rows into CharacterPrepSaveRow.
        await service.SaveAsync(
            submissionId: 1,
            new[] { new CharacterPrepSaveRow(RegistrationId: 1, CharacterName: "Legolas", StartingEquipmentOptionId: 10, CharacterPrepNote: "preferuje luk") },
            NowUtc,
            CancellationToken.None);

        // Assert database persistence.
        await using (var verify = new ApplicationDbContext(options))
        {
            var reg = await verify.Registrations.SingleAsync(r => r.Id == 1);
            Assert.Equal("Legolas", reg.CharacterName);
            Assert.Equal(10, reg.StartingEquipmentOptionId);
            Assert.Equal("preferuje luk", reg.CharacterPrepNote);
            Assert.Equal(NowUtc, reg.CharacterPrepUpdatedAtUtc);
        }

        // Assert that a second GET of the page would show the pre-filled values.
        var view = await service.GetPrepViewAsync(submissionId: 1, NowUtc, CancellationToken.None);
        Assert.NotNull(view);
        var row = Assert.Single(view!.Rows);
        Assert.Equal("Legolas", row.CharacterName);
        Assert.Equal(10, row.StartingEquipmentOptionId);
        Assert.Equal("preferuje luk", row.CharacterPrepNote);
        Assert.NotNull(row.UpdatedAtUtc);
    }

    // ----- Task 14: read-only after game start -----

    [Fact]
    public async Task View_is_readonly_when_game_started()
    {
        var options = CreateOptions();
        await SeedAsync(options, token: "t", gameStartDaysFromNow: -1); // started yesterday

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var view = await service.GetPrepViewAsync(submissionId: 1, NowUtc, CancellationToken.None);

        Assert.NotNull(view);
        Assert.True(view!.IsReadOnly);
    }

    [Fact]
    public async Task View_is_editable_when_game_not_yet_started()
    {
        var options = CreateOptions();
        await SeedAsync(options, token: "t", gameStartDaysFromNow: 30);

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var view = await service.GetPrepViewAsync(submissionId: 1, NowUtc, CancellationToken.None);

        Assert.NotNull(view);
        Assert.False(view!.IsReadOnly);
    }

    // ----- Shared fixture -----

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static async Task SeedAsync(
        DbContextOptions<ApplicationDbContext> options,
        string? token,
        int gameStartDaysFromNow = 30)
    {
        await using var db = new ApplicationDbContext(options);
        db.Users.Add(new ApplicationUser
        {
            Id = "user-1",
            DisplayName = "Parent",
            Email = "parent@example.cz",
            NormalizedEmail = "PARENT@EXAMPLE.CZ",
            UserName = "parent@example.cz",
            NormalizedUserName = "PARENT@EXAMPLE.CZ",
            EmailConfirmed = true,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = FixedUtc
        });
        db.Games.Add(new Game
        {
            Id = 1,
            Name = "Ovčina 2026",
            StartsAtUtc = FixedUtc.AddDays(gameStartDaysFromNow),
            EndsAtUtc = FixedUtc.AddDays(gameStartDaysFromNow + 2),
            RegistrationClosesAtUtc = FixedUtc.AddDays(Math.Max(1, gameStartDaysFromNow - 10)),
            MealOrderingClosesAtUtc = FixedUtc.AddDays(Math.Max(1, gameStartDaysFromNow - 15)),
            PaymentDueAtUtc = FixedUtc.AddDays(Math.Max(1, gameStartDaysFromNow - 5)),
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
            Id = 10, GameId = 1, Key = "tesak", DisplayName = "Tesák", Description = "3/1", SortOrder = 1
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
            CharacterPrepToken = token
        });
        db.People.Add(new Person
        {
            Id = 1, FirstName = "Kid", LastName = "One", BirthYear = 2015,
            CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
        });
        db.Registrations.Add(new Registration
        {
            Id = 1, SubmissionId = 1, PersonId = 1,
            AttendeeType = AttendeeType.Player,
            Status = RegistrationStatus.Active,
            CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
        });
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
