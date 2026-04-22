using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Roles;

namespace RegistraceOvcina.Web.Tests.Features.Roles;

public sealed class GameRoleAccountServiceTests
{
    private const string ActorId = "actor-1";

    // 1 ----------------------------------------------------------------------------------------
    [Fact]
    public async Task LinkOrCreateAccountAsync_missing_email_returns_MissingEmail()
    {
        var options = CreateOptions();
        int personId;

        await using (var db = new ApplicationDbContext(options))
        {
            var person = new Person
            {
                FirstName = "Alice",
                LastName = "NoEmail",
                BirthYear = 1990,
                Email = null,
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(person);
            await db.SaveChangesAsync();
            personId = person.Id;
        }

        var creator = new FakeStubAccountCreator(options);
        var service = CreateService(options, creator);

        var result = await service.LinkOrCreateAccountAsync(personId, ActorId, CancellationToken.None);

        Assert.Equal(LinkOrCreateOutcome.MissingEmail, result.Outcome);
        Assert.Null(result.UserId);
        Assert.Equal(0, creator.CallCount);

        await using var verify = new ApplicationDbContext(options);
        Assert.Empty(verify.AuditLogs);
    }

    // 2 ----------------------------------------------------------------------------------------
    [Fact]
    public async Task LinkOrCreateAccountAsync_existing_matching_user_sets_PersonId_and_returns_Linked()
    {
        var options = CreateOptions();
        int personId;

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(CreateUser("user-1", "Alice Existing", "alice@example.cz"));
            var person = new Person
            {
                FirstName = "Alice",
                LastName = "Existing",
                BirthYear = 1990,
                Email = "alice@example.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(person);
            await db.SaveChangesAsync();
            personId = person.Id;
        }

        var creator = new FakeStubAccountCreator(options);
        var service = CreateService(options, creator);

        var result = await service.LinkOrCreateAccountAsync(personId, ActorId, CancellationToken.None);

        Assert.Equal(LinkOrCreateOutcome.Linked, result.Outcome);
        Assert.Equal("user-1", result.UserId);
        Assert.Equal(0, creator.CallCount);

        await using var verify = new ApplicationDbContext(options);
        var user = await verify.Users.SingleAsync(u => u.Id == "user-1");
        Assert.Equal(personId, user.PersonId);
    }

    // 3 ----------------------------------------------------------------------------------------
    [Fact]
    public async Task LinkOrCreateAccountAsync_no_matching_user_creates_new_one_with_EmailConfirmed_false()
    {
        var options = CreateOptions();
        int personId;

        await using (var db = new ApplicationDbContext(options))
        {
            var person = new Person
            {
                FirstName = "Bob",
                LastName = "NewStub",
                BirthYear = 1985,
                Email = "bob@example.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(person);
            await db.SaveChangesAsync();
            personId = person.Id;
        }

        var creator = new FakeStubAccountCreator(options);
        var service = CreateService(options, creator);

        var result = await service.LinkOrCreateAccountAsync(personId, ActorId, CancellationToken.None);

        Assert.Equal(LinkOrCreateOutcome.Created, result.Outcome);
        Assert.NotNull(result.UserId);
        Assert.Equal(1, creator.CallCount);

        await using var verify = new ApplicationDbContext(options);
        var user = await verify.Users.SingleAsync(u => u.Id == result.UserId);
        Assert.False(user.EmailConfirmed);
        Assert.Equal(personId, user.PersonId);
        Assert.Equal("bob@example.cz", user.Email);
        Assert.Equal("BOB@EXAMPLE.CZ", user.NormalizedEmail);
    }

    // 4 ----------------------------------------------------------------------------------------
    [Fact]
    public async Task LinkOrCreateAccountAsync_existing_user_linked_to_other_Person_returns_Conflict()
    {
        var options = CreateOptions();
        int targetPersonId;

        await using (var db = new ApplicationDbContext(options))
        {
            // An unrelated "other" Person owns the existing user.
            var otherPerson = new Person
            {
                FirstName = "Owner",
                LastName = "OfEmail",
                BirthYear = 1975,
                Email = "shared@example.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(otherPerson);
            await db.SaveChangesAsync();

            var user = CreateUser("user-1", "Owner OfEmail", "shared@example.cz");
            user.PersonId = otherPerson.Id;
            db.Users.Add(user);

            // Target Person that happens to share the email with the other person.
            var targetPerson = new Person
            {
                FirstName = "Target",
                LastName = "SameEmail",
                BirthYear = 1990,
                Email = "shared@example.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(targetPerson);
            await db.SaveChangesAsync();
            targetPersonId = targetPerson.Id;
        }

        var creator = new FakeStubAccountCreator(options);
        var service = CreateService(options, creator);

        var result = await service.LinkOrCreateAccountAsync(targetPersonId, ActorId, CancellationToken.None);

        Assert.Equal(LinkOrCreateOutcome.ConflictEmailUsedByAnotherPerson, result.Outcome);
        Assert.Equal(0, creator.CallCount);

        // No state mutation on the existing user.
        await using var verify = new ApplicationDbContext(options);
        var existing = await verify.Users.SingleAsync(u => u.Id == "user-1");
        Assert.NotEqual(targetPersonId, existing.PersonId);
        Assert.Empty(verify.AuditLogs);
    }

    // 5 ----------------------------------------------------------------------------------------
    [Fact]
    public async Task LinkOrCreateAccountAsync_person_already_linked_returns_AlreadyLinked()
    {
        var options = CreateOptions();
        int personId;

        await using (var db = new ApplicationDbContext(options))
        {
            var person = new Person
            {
                FirstName = "Carla",
                LastName = "Linked",
                BirthYear = 1990,
                Email = "carla@example.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(person);
            await db.SaveChangesAsync();
            personId = person.Id;

            var user = CreateUser("user-1", "Carla Linked", "carla@example.cz");
            user.PersonId = personId;
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var creator = new FakeStubAccountCreator(options);
        var service = CreateService(options, creator);

        var result = await service.LinkOrCreateAccountAsync(personId, ActorId, CancellationToken.None);

        Assert.Equal(LinkOrCreateOutcome.AlreadyLinked, result.Outcome);
        Assert.Equal("user-1", result.UserId);
        Assert.Equal(0, creator.CallCount);

        await using var verify = new ApplicationDbContext(options);
        Assert.Empty(verify.AuditLogs);
    }

    // 6 ----------------------------------------------------------------------------------------
    [Fact]
    public async Task LinkOrCreateAccountAsync_writes_auditlog_for_both_Linked_and_Created()
    {
        var options = CreateOptions();
        int linkedPersonId;
        int createdPersonId;

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(CreateUser("user-1", "Alice L", "alice@example.cz"));
            var linkedPerson = new Person
            {
                FirstName = "Alice",
                LastName = "Linked",
                BirthYear = 1990,
                Email = "alice@example.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(linkedPerson);

            var newPerson = new Person
            {
                FirstName = "Bob",
                LastName = "Created",
                BirthYear = 1985,
                Email = "bob@example.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(newPerson);

            await db.SaveChangesAsync();
            linkedPersonId = linkedPerson.Id;
            createdPersonId = newPerson.Id;
        }

        var creator = new FakeStubAccountCreator(options);
        var service = CreateService(options, creator);

        var linkResult = await service.LinkOrCreateAccountAsync(linkedPersonId, ActorId, CancellationToken.None);
        var createResult = await service.LinkOrCreateAccountAsync(createdPersonId, ActorId, CancellationToken.None);

        Assert.Equal(LinkOrCreateOutcome.Linked, linkResult.Outcome);
        Assert.Equal(LinkOrCreateOutcome.Created, createResult.Outcome);

        await using var verify = new ApplicationDbContext(options);
        var audits = await verify.AuditLogs
            .OrderBy(a => a.Id)
            .ToListAsync();

        Assert.Equal(2, audits.Count);

        var linkAudit = audits[0];
        Assert.Equal("ApplicationUser", linkAudit.EntityType);
        Assert.Equal("user-1", linkAudit.EntityId);
        Assert.Equal("LinkAccount", linkAudit.Action);
        Assert.Equal(ActorId, linkAudit.ActorUserId);
        Assert.Contains("ExactEmailMatch", linkAudit.DetailsJson!);

        var createAudit = audits[1];
        Assert.Equal("ApplicationUser", createAudit.EntityType);
        Assert.Equal("CreateAccount", createAudit.Action);
        Assert.Equal(ActorId, createAudit.ActorUserId);
        Assert.Contains("LinkOrCreate stub", createAudit.DetailsJson!);
    }

    // 7 ----------------------------------------------------------------------------------------
    [Fact]
    public async Task LinkOrCreateAccountsForGameAsync_aggregates_outcomes_correctly()
    {
        var options = CreateOptions();
        const int gameId = 42;

        await using (var db = new ApplicationDbContext(options))
        {
            db.Games.Add(CreateGame(gameId));

            // Three adults:
            //  - adult A: email matches existing unlinked user → Linked
            //  - adult B: email with no matching user → Created
            //  - adult C: no email → skipped upfront (not counted anywhere)
            var adultA = SeedAdult(db, gameId, "Aneta", "Matching", "aneta@example.cz");
            var adultB = SeedAdult(db, gameId, "Bohuslav", "New", "bohu@example.cz");
            _ = SeedAdult(db, gameId, "Cecilia", "NoEmail", null);

            db.Users.Add(CreateUser("user-matches-A", "Aneta Matching", "aneta@example.cz"));
            await db.SaveChangesAsync();
            _ = adultA;
            _ = adultB;
        }

        var creator = new FakeStubAccountCreator(options);
        var service = CreateService(options, creator);

        var bulk = await service.LinkOrCreateAccountsForGameAsync(gameId, ActorId, CancellationToken.None);

        Assert.Equal(1, bulk.Linked);
        Assert.Equal(1, bulk.Created);
        Assert.Equal(0, bulk.AlreadyLinked);
        Assert.Equal(0, bulk.Conflicts);
        // Adults without email are filtered out of the target set entirely, so MissingEmail stays 0.
        Assert.Equal(0, bulk.MissingEmail);
        Assert.Empty(bulk.FirstErrors);
    }

    // 8 ----------------------------------------------------------------------------------------
    [Fact]
    public async Task LinkOrCreateAccountsForGameAsync_skips_adults_without_email()
    {
        var options = CreateOptions();
        const int gameId = 42;

        await using (var db = new ApplicationDbContext(options))
        {
            db.Games.Add(CreateGame(gameId));
            _ = SeedAdult(db, gameId, "OnlyOne", "NoEmail", null);
            await db.SaveChangesAsync();
        }

        var creator = new FakeStubAccountCreator(options);
        var service = CreateService(options, creator);

        var bulk = await service.LinkOrCreateAccountsForGameAsync(gameId, ActorId, CancellationToken.None);

        Assert.Equal(0, bulk.Linked);
        Assert.Equal(0, bulk.Created);
        Assert.Equal(0, bulk.AlreadyLinked);
        Assert.Equal(0, bulk.MissingEmail);
        Assert.Equal(0, bulk.Conflicts);
        Assert.Equal(0, creator.CallCount);

        await using var verify = new ApplicationDbContext(options);
        Assert.Empty(verify.Users);
        Assert.Empty(verify.AuditLogs);
    }

    // 9 ----------------------------------------------------------------------------------------
    [Fact]
    public async Task LinkOrCreateAccountsForGameAsync_skips_adults_already_linked()
    {
        var options = CreateOptions();
        const int gameId = 42;

        await using (var db = new ApplicationDbContext(options))
        {
            db.Games.Add(CreateGame(gameId));
            var person = SeedAdult(db, gameId, "Already", "Linked", "already@example.cz");
            await db.SaveChangesAsync();

            var user = CreateUser("user-1", "Already Linked", "already@example.cz");
            user.PersonId = person.Id;
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var creator = new FakeStubAccountCreator(options);
        var service = CreateService(options, creator);

        var bulk = await service.LinkOrCreateAccountsForGameAsync(gameId, ActorId, CancellationToken.None);

        // Already-linked adults are filtered out before iteration, so no counters move at all.
        Assert.Equal(0, bulk.Linked);
        Assert.Equal(0, bulk.Created);
        Assert.Equal(0, bulk.AlreadyLinked);
        Assert.Equal(0, creator.CallCount);

        await using var verify = new ApplicationDbContext(options);
        Assert.Empty(verify.AuditLogs);
    }

    // -------- helpers --------

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static GameRoleAccountService CreateService(
        DbContextOptions<ApplicationDbContext> options,
        IStubAccountCreator creator)
        => new(
            new TestDbContextFactory(options),
            creator,
            new FixedTimeProvider(),
            NullLogger<GameRoleAccountService>.Instance);

    private static ApplicationUser CreateUser(string id, string displayName, string email) => new()
    {
        Id = id,
        DisplayName = displayName,
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        EmailConfirmed = true,
        IsActive = true,
        SecurityStamp = "initial-stamp",
        CreatedAtUtc = FixedDate()
    };

    private static Game CreateGame(int id) => new()
    {
        Id = id,
        Name = "Test Game",
        Description = "",
        BankAccount = "1234/5678",
        BankAccountName = "Pořadatel",
        StartsAtUtc = FixedDate().AddMonths(2),
        EndsAtUtc = FixedDate().AddMonths(2).AddDays(2),
        RegistrationClosesAtUtc = FixedDate().AddMonths(1),
        MealOrderingClosesAtUtc = FixedDate().AddMonths(1),
        PaymentDueAtUtc = FixedDate().AddMonths(1),
        PlayerBasePrice = 100m,
        SecondChildPrice = 80m,
        ThirdPlusChildPrice = 60m,
        AdultHelperBasePrice = 50m,
        LodgingIndoorPrice = 0m,
        LodgingOutdoorPrice = 0m,
        VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId
    };

    private static Person SeedAdult(
        ApplicationDbContext db,
        int gameId,
        string first,
        string last,
        string? email)
    {
        var person = new Person
        {
            FirstName = first,
            LastName = last,
            BirthYear = 1990,
            Email = email,
            CreatedAtUtc = FixedDate(),
            UpdatedAtUtc = FixedDate()
        };
        db.People.Add(person);
        db.SaveChanges();

        var submission = new RegistrationSubmission
        {
            GameId = gameId,
            RegistrantUserId = "registrant-" + Guid.NewGuid().ToString("N"),
            PrimaryContactName = $"{first} {last}",
            GroupName = $"Rodina {last}",
            PrimaryEmail = email ?? "nobody@example.cz",
            PrimaryPhone = "+420 111 222 333",
            Status = SubmissionStatus.Submitted,
            LastEditedAtUtc = FixedDate()
        };
        db.RegistrationSubmissions.Add(submission);
        db.SaveChanges();

        db.Registrations.Add(new Registration
        {
            SubmissionId = submission.Id,
            PersonId = person.Id,
            AttendeeType = AttendeeType.Adult,
            Status = RegistrationStatus.Active,
            CreatedAtUtc = FixedDate(),
            UpdatedAtUtc = FixedDate()
        });
        db.SaveChanges();

        return person;
    }

    private static DateTime FixedDate() => new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ApplicationDbContext(options));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now = new(new DateTime(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc));
        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>
    /// Test stub: creates an ApplicationUser directly in the DbContext so we don't need to spin
    /// up the full ASP.NET Core Identity stack (UserManager, UserStore, PasswordHasher, etc.)
    /// in unit tests. Mirrors the behavior of <see cref="IdentityStubAccountCreator"/> in prod.
    /// </summary>
    private sealed class FakeStubAccountCreator(DbContextOptions<ApplicationDbContext> options)
        : IStubAccountCreator
    {
        public int CallCount { get; private set; }

        public async Task<StubAccountCreateResult> CreateStubAsync(
            string email,
            string displayName,
            CancellationToken ct)
        {
            CallCount++;

            await using var db = new ApplicationDbContext(options);
            var id = Guid.NewGuid().ToString("N");
            var user = new ApplicationUser
            {
                Id = id,
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                DisplayName = displayName,
                EmailConfirmed = false,
                IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = FixedDate(),
                PasswordHash = "stub-password-hash"
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);

            return new StubAccountCreateResult(true, user.Id, null);
        }
    }
}
