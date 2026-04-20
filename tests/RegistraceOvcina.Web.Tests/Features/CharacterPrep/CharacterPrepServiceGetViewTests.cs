using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.CharacterPrep;

namespace RegistraceOvcina.Web.Tests.Features.CharacterPrep;

public sealed class CharacterPrepServiceGetViewTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset NowUtc = new(FixedUtc);

    [Fact]
    public async Task Returns_only_Player_attendees()
    {
        var options = CreateOptions();
        var submissionId = await SeedMixedAttendeesAsync(options);

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var submission = await LoadSubmissionAsync(options, submissionId);

        var view = await service.GetPrepViewAsync(submission, NowUtc, CancellationToken.None);

        Assert.All(view.Rows, r => Assert.DoesNotContain("Adult", r.PersonFullName));
        Assert.Equal(2, view.Rows.Count);
        Assert.Contains(view.Rows, r => r.PersonFullName == "Adam Player");
        Assert.Contains(view.Rows, r => r.PersonFullName == "Bea Player");
    }

    [Fact]
    public async Task Rows_are_ordered_by_LastName_then_FirstName()
    {
        var options = CreateOptions();
        var submissionId = await SeedUnsortedPlayersAsync(options);

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var submission = await LoadSubmissionAsync(options, submissionId);

        var view = await service.GetPrepViewAsync(submission, NowUtc, CancellationToken.None);

        var names = view.Rows.Select(r => r.PersonFullName).ToList();
        Assert.Equal(new[] { "Anna Adamová", "Bořek Adamů", "Cyril Bárta" }, names);
    }

    [Fact]
    public async Task IsReadOnly_is_true_when_game_has_started()
    {
        var options = CreateOptions();
        var submissionId = await SeedSimplePlayerAsync(options, gameStartOffsetDays: -1);

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var submission = await LoadSubmissionAsync(options, submissionId);

        var view = await service.GetPrepViewAsync(submission, NowUtc, CancellationToken.None);

        Assert.True(view.IsReadOnly);
    }

    [Fact]
    public async Task Options_are_ordered_by_SortOrder()
    {
        var options = CreateOptions();
        var submissionId = await SeedSimplePlayerAsync(options, gameStartOffsetDays: 30);

        await using (var db = new ApplicationDbContext(options))
        {
            db.StartingEquipmentOptions.AddRange(
                new StartingEquipmentOption { Id = 1, GameId = 1, Key = "z", DisplayName = "Z", SortOrder = 20 },
                new StartingEquipmentOption { Id = 2, GameId = 1, Key = "a", DisplayName = "A", SortOrder = 10 },
                new StartingEquipmentOption { Id = 3, GameId = 1, Key = "m", DisplayName = "M", SortOrder = 15 });
            await db.SaveChangesAsync();
        }

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var submission = await LoadSubmissionAsync(options, submissionId);

        var view = await service.GetPrepViewAsync(submission, NowUtc, CancellationToken.None);

        Assert.Equal(new[] { "A", "M", "Z" }, view.Options.Select(x => x.DisplayName));
    }

    [Fact]
    public async Task Preexisting_values_roundtrip_into_view()
    {
        var options = CreateOptions();
        var submissionId = await SeedSimplePlayerAsync(options, gameStartOffsetDays: 30);

        await using (var db = new ApplicationDbContext(options))
        {
            var option = new StartingEquipmentOption
            {
                Id = 10, GameId = 1, Key = "tesak", DisplayName = "Tesák (3/1)", SortOrder = 1
            };
            db.StartingEquipmentOptions.Add(option);
            var registration = await db.Registrations.SingleAsync();
            registration.CharacterName = "Elrond";
            registration.StartingEquipmentOptionId = option.Id;
            registration.CharacterPrepNote = "preferuje luk";
            registration.CharacterPrepUpdatedAtUtc = NowUtc.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        var submission = await LoadSubmissionAsync(options, submissionId);

        var view = await service.GetPrepViewAsync(submission, NowUtc, CancellationToken.None);

        var row = Assert.Single(view.Rows);
        Assert.Equal("Elrond", row.CharacterName);
        Assert.Equal(10, row.StartingEquipmentOptionId);
        Assert.Equal("preferuje luk", row.CharacterPrepNote);
        Assert.NotNull(row.UpdatedAtUtc);
    }

    private static async Task<RegistrationSubmission> LoadSubmissionAsync(
        DbContextOptions<ApplicationDbContext> options,
        int submissionId)
    {
        await using var db = new ApplicationDbContext(options);
        return await db.RegistrationSubmissions.SingleAsync(x => x.Id == submissionId);
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static async Task<int> SeedMixedAttendeesAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);
        SeedBase(db, gameStartOffsetDays: 30);

        db.People.AddRange(
            MakePerson(1, "Adam", "Player"),
            MakePerson(2, "Bea", "Player"),
            MakePerson(3, "Cíla", "Adult"));
        await db.SaveChangesAsync();

        db.Registrations.AddRange(
            MakeReg(1, personId: 1, AttendeeType.Player),
            MakeReg(2, personId: 2, AttendeeType.Player),
            MakeReg(3, personId: 3, AttendeeType.Adult));
        await db.SaveChangesAsync();
        return 1;
    }

    private static async Task<int> SeedUnsortedPlayersAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);
        SeedBase(db, gameStartOffsetDays: 30);

        db.People.AddRange(
            MakePerson(1, "Cyril", "Bárta"),
            MakePerson(2, "Anna", "Adamová"),
            MakePerson(3, "Bořek", "Adamů"));
        await db.SaveChangesAsync();

        db.Registrations.AddRange(
            MakeReg(1, personId: 1, AttendeeType.Player),
            MakeReg(2, personId: 2, AttendeeType.Player),
            MakeReg(3, personId: 3, AttendeeType.Player));
        await db.SaveChangesAsync();
        return 1;
    }

    private static async Task<int> SeedSimplePlayerAsync(
        DbContextOptions<ApplicationDbContext> options,
        int gameStartOffsetDays)
    {
        await using var db = new ApplicationDbContext(options);
        SeedBase(db, gameStartOffsetDays);

        db.People.Add(MakePerson(1, "Player", "Zero"));
        await db.SaveChangesAsync();

        db.Registrations.Add(MakeReg(1, personId: 1, AttendeeType.Player));
        await db.SaveChangesAsync();
        return 1;
    }

    private static void SeedBase(ApplicationDbContext db, int gameStartOffsetDays)
    {
        var user = new ApplicationUser
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
        };
        var game = new Game
        {
            Id = 1,
            Name = "Ovčina 2026",
            StartsAtUtc = FixedUtc.AddDays(gameStartOffsetDays),
            EndsAtUtc = FixedUtc.AddDays(gameStartOffsetDays + 2),
            RegistrationClosesAtUtc = FixedUtc.AddDays(gameStartOffsetDays - 10),
            MealOrderingClosesAtUtc = FixedUtc.AddDays(gameStartOffsetDays - 15),
            PaymentDueAtUtc = FixedUtc.AddDays(gameStartOffsetDays - 5),
            PlayerBasePrice = 1200,
            AdultHelperBasePrice = 800,
            BankAccount = "123/0100",
            BankAccountName = "Ovčina",
            VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
            IsPublished = true,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        };
        var submission = new RegistrationSubmission
        {
            Id = 1,
            GameId = 1,
            RegistrantUserId = user.Id,
            PrimaryContactName = "Parent",
            PrimaryEmail = "parent@example.cz",
            PrimaryPhone = "777111222",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200
        };
        db.Users.Add(user);
        db.Games.Add(game);
        db.RegistrationSubmissions.Add(submission);
        db.SaveChanges();
    }

    private static Person MakePerson(int id, string first, string last) =>
        new()
        {
            Id = id,
            FirstName = first,
            LastName = last,
            BirthYear = 2015,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        };

    private static Registration MakeReg(int id, int personId, AttendeeType type) =>
        new()
        {
            Id = id,
            SubmissionId = 1,
            PersonId = personId,
            AttendeeType = type,
            Status = RegistrationStatus.Active,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        };

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ApplicationDbContext(options));
    }
}
