using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.CharacterPrep;

namespace RegistraceOvcina.Web.Tests.Features.CharacterPrep;

/// <summary>
/// Covers the organizer-facing CRUD service for StartingEquipmentOption rows
/// (Phase 6 / Task 19). The service wraps validation, duplicate-key handling,
/// a reference guard on delete, and a cross-game copy helper.
/// </summary>
public sealed class CharacterPrepOptionsServiceTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ListAsync_returns_options_ordered_by_SortOrder()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await using (var db = new ApplicationDbContext(options))
        {
            db.StartingEquipmentOptions.Add(new StartingEquipmentOption
            {
                Id = 1, GameId = 1, Key = "luk", DisplayName = "Luk", SortOrder = 20
            });
            db.StartingEquipmentOptions.Add(new StartingEquipmentOption
            {
                Id = 2, GameId = 1, Key = "tesak", DisplayName = "Tesák", SortOrder = 10
            });
            db.StartingEquipmentOptions.Add(new StartingEquipmentOption
            {
                Id = 3, GameId = 1, Key = "mec", DisplayName = "Meč", SortOrder = 30
            });
            await db.SaveChangesAsync();
        }

        var service = new CharacterPrepOptionsService(new TestDbContextFactory(options), NullLogger());

        var list = await service.ListAsync(1, CancellationToken.None);

        Assert.Equal(new[] { "tesak", "luk", "mec" }, list.Select(x => x.Key).ToArray());
    }

    [Fact]
    public async Task CreateAsync_persists_option()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);

        var service = new CharacterPrepOptionsService(new TestDbContextFactory(options), NullLogger());

        var created = await service.CreateAsync(1, "tesak", "Tesák", "3/1", 5, CancellationToken.None);

        Assert.True(created.Id > 0);
        Assert.Equal(1, created.GameId);
        Assert.Equal("tesak", created.Key);
        Assert.Equal("Tesák", created.DisplayName);
        Assert.Equal("3/1", created.Description);
        Assert.Equal(5, created.SortOrder);

        await using var verify = new ApplicationDbContext(options);
        Assert.Single(await verify.StartingEquipmentOptions.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_trims_and_lowercases_Key()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);

        var service = new CharacterPrepOptionsService(new TestDbContextFactory(options), NullLogger());

        var created = await service.CreateAsync(1, "  TESAK  ", "Tesák", null, 0, CancellationToken.None);

        Assert.Equal("tesak", created.Key);
    }

    [Fact]
    public async Task CreateAsync_rejects_duplicate_key_in_same_game()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);

        var service = new CharacterPrepOptionsService(new TestDbContextFactory(options), NullLogger());

        await service.CreateAsync(1, "tesak", "Tesák", null, 0, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(1, "TESAK", "Druhý tesák", null, 1, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_allows_same_key_in_different_game()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedGameAsync(options, gameId: 2);

        var service = new CharacterPrepOptionsService(new TestDbContextFactory(options), NullLogger());

        await service.CreateAsync(1, "tesak", "Tesák", null, 0, CancellationToken.None);
        var second = await service.CreateAsync(2, "tesak", "Tesák v druhé hře", null, 0, CancellationToken.None);

        Assert.Equal(2, second.GameId);
        Assert.Equal("tesak", second.Key);
    }

    [Fact]
    public async Task CreateAsync_rejects_empty_key()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);

        var service = new CharacterPrepOptionsService(new TestDbContextFactory(options), NullLogger());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(1, "   ", "Displej", null, 0, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_rejects_empty_displayName()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);

        var service = new CharacterPrepOptionsService(new TestDbContextFactory(options), NullLogger());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(1, "tesak", "   ", null, 0, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_modifies_DisplayName_Description_SortOrder()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await using (var db = new ApplicationDbContext(options))
        {
            db.StartingEquipmentOptions.Add(new StartingEquipmentOption
            {
                Id = 1, GameId = 1, Key = "tesak", DisplayName = "Tesák", Description = "3/1", SortOrder = 5
            });
            await db.SaveChangesAsync();
        }

        var service = new CharacterPrepOptionsService(new TestDbContextFactory(options), NullLogger());

        await service.UpdateAsync(1, "Krátký tesák", "3/2", 10, CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var row = await verify.StartingEquipmentOptions.SingleAsync(x => x.Id == 1);
        Assert.Equal("Krátký tesák", row.DisplayName);
        Assert.Equal("3/2", row.Description);
        Assert.Equal(10, row.SortOrder);
    }

    [Fact]
    public async Task UpdateAsync_does_not_modify_Key()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await using (var db = new ApplicationDbContext(options))
        {
            db.StartingEquipmentOptions.Add(new StartingEquipmentOption
            {
                Id = 1, GameId = 1, Key = "tesak", DisplayName = "Tesák", SortOrder = 5
            });
            await db.SaveChangesAsync();
        }

        var service = new CharacterPrepOptionsService(new TestDbContextFactory(options), NullLogger());

        await service.UpdateAsync(1, "Nový display", null, 99, CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var row = await verify.StartingEquipmentOptions.SingleAsync(x => x.Id == 1);
        Assert.Equal("tesak", row.Key);
    }

    [Fact]
    public async Task UpdateAsync_throws_if_option_missing()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);

        var service = new CharacterPrepOptionsService(new TestDbContextFactory(options), NullLogger());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(9999, "Neco", null, 0, CancellationToken.None));
    }

    [Fact]
    public async Task TryDeleteAsync_returns_false_when_referenced()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await using (var db = new ApplicationDbContext(options))
        {
            db.StartingEquipmentOptions.Add(new StartingEquipmentOption
            {
                Id = 1, GameId = 1, Key = "tesak", DisplayName = "Tesák", SortOrder = 5
            });
            db.People.Add(new Person
            {
                Id = 1, FirstName = "Kid", LastName = "One", BirthYear = 2015,
                CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
            });
            db.RegistrationSubmissions.Add(new RegistrationSubmission
            {
                Id = 1,
                GameId = 1,
                RegistrantUserId = "user-1",
                PrimaryContactName = "Parent One",
                PrimaryEmail = "p@example.cz",
                PrimaryPhone = "+420000000000",
                Status = SubmissionStatus.Submitted,
                SubmittedAtUtc = FixedUtc,
                LastEditedAtUtc = FixedUtc,
                ExpectedTotalAmount = 0
            });
            db.Registrations.Add(new Registration
            {
                Id = 1, SubmissionId = 1, PersonId = 1,
                AttendeeType = AttendeeType.Player,
                Status = RegistrationStatus.Active,
                StartingEquipmentOptionId = 1,
                CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
            });
            await db.SaveChangesAsync();
        }

        var service = new CharacterPrepOptionsService(new TestDbContextFactory(options), NullLogger());

        var deleted = await service.TryDeleteAsync(1, CancellationToken.None);

        Assert.False(deleted);
        await using var verify = new ApplicationDbContext(options);
        Assert.True(await verify.StartingEquipmentOptions.AnyAsync(x => x.Id == 1));
    }

    [Fact]
    public async Task TryDeleteAsync_returns_true_and_deletes_when_unreferenced()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await using (var db = new ApplicationDbContext(options))
        {
            db.StartingEquipmentOptions.Add(new StartingEquipmentOption
            {
                Id = 1, GameId = 1, Key = "tesak", DisplayName = "Tesák", SortOrder = 5
            });
            await db.SaveChangesAsync();
        }

        var service = new CharacterPrepOptionsService(new TestDbContextFactory(options), NullLogger());

        var deleted = await service.TryDeleteAsync(1, CancellationToken.None);

        Assert.True(deleted);
        await using var verify = new ApplicationDbContext(options);
        Assert.False(await verify.StartingEquipmentOptions.AnyAsync(x => x.Id == 1));
    }

    [Fact]
    public async Task CopyFromGameAsync_copies_all_options_to_empty_target()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedGameAsync(options, gameId: 2);
        await using (var db = new ApplicationDbContext(options))
        {
            db.StartingEquipmentOptions.Add(new StartingEquipmentOption
            {
                Id = 1, GameId = 1, Key = "tesak", DisplayName = "Tesák", Description = "3/1", SortOrder = 10
            });
            db.StartingEquipmentOptions.Add(new StartingEquipmentOption
            {
                Id = 2, GameId = 1, Key = "luk", DisplayName = "Luk", Description = null, SortOrder = 20
            });
            await db.SaveChangesAsync();
        }

        var service = new CharacterPrepOptionsService(new TestDbContextFactory(options), NullLogger());

        await service.CopyFromGameAsync(sourceGameId: 1, targetGameId: 2, CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var copied = await verify.StartingEquipmentOptions
            .Where(x => x.GameId == 2)
            .OrderBy(x => x.SortOrder)
            .ToListAsync();
        Assert.Equal(2, copied.Count);
        Assert.Equal("tesak", copied[0].Key);
        Assert.Equal("Tesák", copied[0].DisplayName);
        Assert.Equal("3/1", copied[0].Description);
        Assert.Equal(10, copied[0].SortOrder);
        Assert.Equal("luk", copied[1].Key);
    }

    [Fact]
    public async Task CopyFromGameAsync_skips_keys_that_exist_on_target()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, gameId: 1);
        await SeedGameAsync(options, gameId: 2);
        await using (var db = new ApplicationDbContext(options))
        {
            db.StartingEquipmentOptions.Add(new StartingEquipmentOption
            {
                Id = 1, GameId = 1, Key = "tesak", DisplayName = "Tesák (1)", SortOrder = 10
            });
            db.StartingEquipmentOptions.Add(new StartingEquipmentOption
            {
                Id = 2, GameId = 1, Key = "luk", DisplayName = "Luk", SortOrder = 20
            });
            db.StartingEquipmentOptions.Add(new StartingEquipmentOption
            {
                Id = 10, GameId = 2, Key = "tesak", DisplayName = "Tesák already here", SortOrder = 5
            });
            await db.SaveChangesAsync();
        }

        var service = new CharacterPrepOptionsService(new TestDbContextFactory(options), NullLogger());

        await service.CopyFromGameAsync(sourceGameId: 1, targetGameId: 2, CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var target = await verify.StartingEquipmentOptions
            .Where(x => x.GameId == 2)
            .OrderBy(x => x.Key)
            .ToListAsync();
        Assert.Equal(2, target.Count);
        // pre-existing "tesak" must be untouched
        Assert.Equal("Tesák already here", target.Single(x => x.Key == "tesak").DisplayName);
        // "luk" should have been copied in
        Assert.Equal("Luk", target.Single(x => x.Key == "luk").DisplayName);
    }

    // ---------- helpers ----------

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static async Task SeedGameAsync(DbContextOptions<ApplicationDbContext> options, int gameId)
    {
        await using var db = new ApplicationDbContext(options);
        db.Games.Add(new Game
        {
            Id = gameId,
            Name = $"Hra {gameId}",
            StartsAtUtc = FixedUtc.AddDays(30),
            EndsAtUtc = FixedUtc.AddDays(32),
            RegistrationClosesAtUtc = FixedUtc.AddDays(20),
            MealOrderingClosesAtUtc = FixedUtc.AddDays(25),
            PaymentDueAtUtc = FixedUtc.AddDays(20),
            PlayerBasePrice = 1200,
            AdultHelperBasePrice = 800,
            BankAccount = "x",
            BankAccountName = "y",
            VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
            IsPublished = true,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        });
        await db.SaveChangesAsync();
    }

    private static Microsoft.Extensions.Logging.ILogger<CharacterPrepOptionsService> NullLogger() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<CharacterPrepOptionsService>.Instance;

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => new(CreateDbContext());
    }
}
