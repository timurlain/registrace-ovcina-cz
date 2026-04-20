using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.CharacterPrep;

namespace RegistraceOvcina.Web.Tests.Features.CharacterPrep;

public sealed class CharacterPrepServiceSaveTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset NowUtc = new(FixedUtc);

    [Fact]
    public async Task Saves_character_name_equipment_and_note()
    {
        var options = CreateOptions();
        await SeedAsync(options);

        var service = new CharacterPrepService(new TestDbContextFactory(options));

        await service.SaveAsync(
            submissionId: 1,
            new[] { new CharacterPrepSaveRow(1, "Elrond", 10, "preferuje luk") },
            NowUtc,
            CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var reg = await verify.Registrations.SingleAsync(x => x.Id == 1);
        Assert.Equal("Elrond", reg.CharacterName);
        Assert.Equal(10, reg.StartingEquipmentOptionId);
        Assert.Equal("preferuje luk", reg.CharacterPrepNote);
        Assert.Equal(NowUtc, reg.CharacterPrepUpdatedAtUtc);
    }

    [Fact]
    public async Task Stamps_UpdatedAtUtc_only_when_something_changed()
    {
        var options = CreateOptions();
        await SeedAsync(options);
        var firstStamp = NowUtc.AddHours(-5);

        // First save establishes the fields and stamp.
        var service = new CharacterPrepService(new TestDbContextFactory(options));
        await service.SaveAsync(
            submissionId: 1,
            new[] { new CharacterPrepSaveRow(1, "Elrond", 10, "pozn.") },
            firstStamp,
            CancellationToken.None);

        // Second save with identical values should NOT re-stamp.
        await service.SaveAsync(
            submissionId: 1,
            new[] { new CharacterPrepSaveRow(1, "Elrond", 10, "pozn.") },
            NowUtc,
            CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var reg = await verify.Registrations.SingleAsync(x => x.Id == 1);
        Assert.Equal(firstStamp, reg.CharacterPrepUpdatedAtUtc);
    }

    [Fact]
    public async Task Trims_and_nulls_empty_strings()
    {
        var options = CreateOptions();
        await SeedAsync(options);

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        await service.SaveAsync(
            submissionId: 1,
            new[] { new CharacterPrepSaveRow(1, "   Elrond   ", null, "    ") },
            NowUtc,
            CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var reg = await verify.Registrations.SingleAsync(x => x.Id == 1);
        Assert.Equal("Elrond", reg.CharacterName);
        Assert.Null(reg.CharacterPrepNote);
    }

    [Fact]
    public async Task Ignores_rows_for_foreign_submissions()
    {
        var options = CreateOptions();
        await SeedAsync(options);
        await SeedSecondSubmissionAsync(options);

        var service = new CharacterPrepService(new TestDbContextFactory(options));

        // Registration 99 belongs to submission 2, passing it in a save for submission 1 must be ignored.
        await service.SaveAsync(
            submissionId: 1,
            new[] { new CharacterPrepSaveRow(99, "ShouldNotApply", null, null) },
            NowUtc,
            CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var foreign = await verify.Registrations.SingleAsync(x => x.Id == 99);
        Assert.Null(foreign.CharacterName);
        Assert.Null(foreign.CharacterPrepUpdatedAtUtc);
    }

    [Fact]
    public async Task Ignores_rows_for_non_Player_attendees()
    {
        var options = CreateOptions();
        await SeedAsync(options);
        await SeedAdultRegistrationAsync(options);

        var service = new CharacterPrepService(new TestDbContextFactory(options));

        // Registration 2 is an Adult on submission 1; save must silently skip it.
        await service.SaveAsync(
            submissionId: 1,
            new[] { new CharacterPrepSaveRow(2, "ShouldNotApply", null, null) },
            NowUtc,
            CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var adult = await verify.Registrations.SingleAsync(x => x.Id == 2);
        Assert.Null(adult.CharacterName);
        Assert.Null(adult.CharacterPrepUpdatedAtUtc);
    }

    [Fact]
    public async Task Rejects_equipment_option_from_different_game()
    {
        var options = CreateOptions();
        await SeedAsync(options);

        // Equipment option 900 belongs to game 2, submission 1 is for game 1.
        await using (var db = new ApplicationDbContext(options))
        {
            db.Games.Add(new Game
            {
                Id = 2,
                Name = "Other game",
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
                Id = 900, GameId = 2, Key = "foreign", DisplayName = "Foreign", SortOrder = 1
            });
            await db.SaveChangesAsync();
        }

        var service = new CharacterPrepService(new TestDbContextFactory(options));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await service.SaveAsync(
                submissionId: 1,
                new[] { new CharacterPrepSaveRow(1, "x", 900, null) },
                NowUtc,
                CancellationToken.None));
    }

    [Fact]
    public async Task Clearing_all_fields_is_allowed_and_stamps_UpdatedAtUtc()
    {
        var options = CreateOptions();
        await SeedAsync(options);
        var firstStamp = NowUtc.AddHours(-5);

        var service = new CharacterPrepService(new TestDbContextFactory(options));
        await service.SaveAsync(
            submissionId: 1,
            new[] { new CharacterPrepSaveRow(1, "Elrond", 10, "pozn.") },
            firstStamp,
            CancellationToken.None);

        // Now clear every field.
        await service.SaveAsync(
            submissionId: 1,
            new[] { new CharacterPrepSaveRow(1, null, null, null) },
            NowUtc,
            CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var reg = await verify.Registrations.SingleAsync(x => x.Id == 1);
        Assert.Null(reg.CharacterName);
        Assert.Null(reg.StartingEquipmentOptionId);
        Assert.Null(reg.CharacterPrepNote);
        Assert.Equal(NowUtc, reg.CharacterPrepUpdatedAtUtc);
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static async Task SeedAsync(DbContextOptions<ApplicationDbContext> options)
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
            Id = 10, GameId = 1, Key = "tesak", DisplayName = "Tesák", SortOrder = 1
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
            ExpectedTotalAmount = 1200
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

    private static async Task SeedAdultRegistrationAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);
        db.People.Add(new Person
        {
            Id = 2, FirstName = "Parent", LastName = "One", BirthYear = 1985,
            CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
        });
        db.Registrations.Add(new Registration
        {
            Id = 2, SubmissionId = 1, PersonId = 2,
            AttendeeType = AttendeeType.Adult,
            Status = RegistrationStatus.Active,
            CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedSecondSubmissionAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);
        db.Users.Add(new ApplicationUser
        {
            Id = "user-2",
            DisplayName = "Other",
            Email = "other@example.cz",
            NormalizedEmail = "OTHER@EXAMPLE.CZ",
            UserName = "other@example.cz",
            NormalizedUserName = "OTHER@EXAMPLE.CZ",
            EmailConfirmed = true,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = FixedUtc
        });
        db.RegistrationSubmissions.Add(new RegistrationSubmission
        {
            Id = 2,
            GameId = 1,
            RegistrantUserId = "user-2",
            PrimaryContactName = "Other",
            PrimaryEmail = "other@example.cz",
            PrimaryPhone = "777222333",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200
        });
        db.People.Add(new Person
        {
            Id = 99, FirstName = "Other", LastName = "Kid", BirthYear = 2015,
            CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
        });
        db.Registrations.Add(new Registration
        {
            Id = 99, SubmissionId = 2, PersonId = 99,
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
