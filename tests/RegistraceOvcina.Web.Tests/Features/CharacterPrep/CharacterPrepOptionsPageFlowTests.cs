using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.CharacterPrep;

namespace RegistraceOvcina.Web.Tests.Features.CharacterPrep;

/// <summary>
/// Service-level flow tests that mirror what the
/// <c>/organizace/hry/{GameId}/priprava-postav/vybava</c> page does through
/// CharacterPrepOptionsService (Phase 6 / Task 19). Covers list → add → edit →
/// delete plus the delete-blocked-by-reference path and copy-from-another-game.
/// Kept at the service layer because the project has no bUnit/WebApplicationFactory
/// harness yet.
/// </summary>
public sealed class CharacterPrepOptionsPageFlowTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Happy_path_list_add_edit_delete()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, 1);

        var service = new CharacterPrepOptionsService(
            new TestDbContextFactory(options),
            NullLogger<CharacterPrepOptionsService>.Instance);

        // 1. List — starts empty.
        Assert.Empty(await service.ListAsync(1, CancellationToken.None));

        // 2. Add.
        var created = await service.CreateAsync(1, "tesak", "Tesák", "3/1", 10, CancellationToken.None);
        var listAfterAdd = await service.ListAsync(1, CancellationToken.None);
        Assert.Single(listAfterAdd);
        Assert.Equal("tesak", listAfterAdd[0].Key);

        // 3. Edit — DisplayName/Description/SortOrder change, Key stable.
        await service.UpdateAsync(created.Id, "Krátký tesák", "3/2", 20, CancellationToken.None);
        var listAfterEdit = await service.ListAsync(1, CancellationToken.None);
        Assert.Equal("tesak", listAfterEdit[0].Key);
        Assert.Equal("Krátký tesák", listAfterEdit[0].DisplayName);
        Assert.Equal("3/2", listAfterEdit[0].Description);
        Assert.Equal(20, listAfterEdit[0].SortOrder);

        // 4. Delete — succeeds because not referenced.
        Assert.True(await service.TryDeleteAsync(created.Id, CancellationToken.None));
        Assert.Empty(await service.ListAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_is_blocked_when_option_is_in_use()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, 1);
        int optionId;
        await using (var db = new ApplicationDbContext(options))
        {
            var opt = new StartingEquipmentOption
            {
                GameId = 1, Key = "tesak", DisplayName = "Tesák", SortOrder = 1
            };
            db.StartingEquipmentOptions.Add(opt);
            await db.SaveChangesAsync();
            optionId = opt.Id;

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
                PrimaryContactName = "Parent",
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
                StartingEquipmentOptionId = optionId,
                CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
            });
            await db.SaveChangesAsync();
        }

        var service = new CharacterPrepOptionsService(
            new TestDbContextFactory(options),
            NullLogger<CharacterPrepOptionsService>.Instance);

        var deleted = await service.TryDeleteAsync(optionId, CancellationToken.None);

        Assert.False(deleted);
        // Still there.
        Assert.Single(await service.ListAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task Copy_from_another_game_preserves_existing_and_imports_rest()
    {
        var options = CreateOptions();
        await SeedGameAsync(options, 1);
        await SeedGameAsync(options, 2);
        await using (var db = new ApplicationDbContext(options))
        {
            db.StartingEquipmentOptions.AddRange(
                new StartingEquipmentOption { GameId = 1, Key = "tesak", DisplayName = "Tesák", SortOrder = 1 },
                new StartingEquipmentOption { GameId = 1, Key = "luk", DisplayName = "Luk", SortOrder = 2 },
                new StartingEquipmentOption { GameId = 2, Key = "tesak", DisplayName = "Already here", SortOrder = 9 });
            await db.SaveChangesAsync();
        }

        var service = new CharacterPrepOptionsService(
            new TestDbContextFactory(options),
            NullLogger<CharacterPrepOptionsService>.Instance);

        await service.CopyFromGameAsync(sourceGameId: 1, targetGameId: 2, CancellationToken.None);

        var target = await service.ListAsync(2, CancellationToken.None);
        Assert.Equal(2, target.Count);
        Assert.Equal("Already here", target.Single(x => x.Key == "tesak").DisplayName);
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

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => new(CreateDbContext());
    }
}
