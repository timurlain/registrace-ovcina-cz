using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Integration;
using RegistraceOvcina.Web.Features.Users;

namespace RegistraceOvcina.Web.Tests;

/// <summary>
/// Tests for <see cref="IntegrationApiEndpoints.CheckRegistrationPresenceAsync"/>,
/// the handler behind <c>GET /api/v1/registrations/check</c>. These tests are
/// driven directly against the extracted helper (not the HTTP pipeline),
/// mirroring the pattern used by <see cref="AdultsEndpointTests"/>.
///
/// Scenarios covered:
///   1. Adult attendee with matching Person.Email → isRegistered:true, guardianOnly:false
///      (the Lukáš regression from production).
///   2. Player (child) attendee with matching Person.Email → same.
///   3. Email matches ONLY a RegistrationSubmission.PrimaryEmail → guardianOnly:true.
///   4. Email not in any registration or submission → isRegistered:false, guardianOnly:false.
///   5. Case-insensitive match (LUKAS@test.cz vs lukas@test.cz) → true.
///   6. Whitespace-trimmed input (" lukas@test.cz ") → true.
///   7. Email matches attendee in a DIFFERENT game → false for this game.
///   8. Email matches a soft-deleted submission's row → false (respects IsDeleted filter).
///   9. Fallback via ApplicationUser / UserEmailService when Person.Email is null
///      (the dedup path) → still true.
/// </summary>
public sealed class RegistrationsCheckEndpointTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc);

    // ----- Scenario 1: Adult who registered themselves as an attendee -----
    [Fact]
    public async Task AdultAttendee_WithMatchingPersonEmail_ReturnsTrue_NotGuardianOnly()
    {
        await using var seeded = await SeedAsync(s =>
        {
            var game = s.AddGame(1);
            var lukas = s.AddPerson(100, "Lukáš", "Heinz", 1984, "lukas@test.cz");
            var submission = s.AddSubmission(10, game.Id, registrantUserId: "u-lukas", primaryEmail: "lukas@test.cz");
            s.AddRegistration(1000, submission.Id, lukas.Id, AttendeeType.Adult);
        });

        var result = await Check(seeded, "lukas@test.cz", gameId: 1);

        Assert.True(result.IsRegistered);
        Assert.False(result.GuardianOnly);
    }

    // ----- Scenario 2: Player (child) who is registered -----
    [Fact]
    public async Task PlayerAttendee_WithMatchingPersonEmail_ReturnsTrue_NotGuardianOnly()
    {
        await using var seeded = await SeedAsync(s =>
        {
            var game = s.AddGame(1);
            var kid = s.AddPerson(101, "Tomík", "Heinz", 2015, "tomik@test.cz");
            var submission = s.AddSubmission(10, game.Id, registrantUserId: "u-lukas", primaryEmail: "lukas@test.cz");
            s.AddRegistration(1001, submission.Id, kid.Id, AttendeeType.Player);
        });

        var result = await Check(seeded, "tomik@test.cz", gameId: 1);

        Assert.True(result.IsRegistered);
        Assert.False(result.GuardianOnly);
    }

    // ----- Scenario 3: Email only matches a submission's PrimaryEmail -----
    [Fact]
    public async Task EmailOnlyMatchesSubmissionPrimary_ReturnsTrue_GuardianOnly()
    {
        await using var seeded = await SeedAsync(s =>
        {
            var game = s.AddGame(1);
            // Marek registered his three kids but not himself. His email is only
            // on the submission as the household primary contact.
            var marek = s.AddPerson(200, "Marek", "Štěpán", 1980, "marek@test.cz");
            _ = marek; // parent exists as a Person but has no Registration row
            var kid1 = s.AddPerson(201, "Anna", "Štěpán", 2014, email: null);
            var kid2 = s.AddPerson(202, "Bára", "Štěpán", 2016, email: null);
            var kid3 = s.AddPerson(203, "Cyril", "Štěpán", 2018, email: null);
            var submission = s.AddSubmission(20, game.Id, registrantUserId: "u-marek", primaryEmail: "marek@test.cz");
            s.AddRegistration(2001, submission.Id, kid1.Id, AttendeeType.Player);
            s.AddRegistration(2002, submission.Id, kid2.Id, AttendeeType.Player);
            s.AddRegistration(2003, submission.Id, kid3.Id, AttendeeType.Player);
        });

        var result = await Check(seeded, "marek@test.cz", gameId: 1);

        Assert.True(result.IsRegistered);
        Assert.True(result.GuardianOnly);
    }

    // ----- Scenario 4: Email is not in the game at all -----
    [Fact]
    public async Task UnknownEmail_ReturnsFalse()
    {
        await using var seeded = await SeedAsync(s =>
        {
            var game = s.AddGame(1);
            var someone = s.AddPerson(300, "Jana", "Testová", 1990, "jana@test.cz");
            var submission = s.AddSubmission(30, game.Id, registrantUserId: "u-jana", primaryEmail: "jana@test.cz");
            s.AddRegistration(3001, submission.Id, someone.Id, AttendeeType.Adult);
        });

        var result = await Check(seeded, "stranger@test.cz", gameId: 1);

        Assert.False(result.IsRegistered);
        Assert.False(result.GuardianOnly);
    }

    // ----- Scenario 5: Case-insensitive match -----
    [Fact]
    public async Task CaseInsensitive_UpperCaseInputMatchesLowerCaseStored()
    {
        await using var seeded = await SeedAsync(s =>
        {
            var game = s.AddGame(1);
            var lukas = s.AddPerson(100, "Lukáš", "Heinz", 1984, "lukas@test.cz");
            var submission = s.AddSubmission(10, game.Id, registrantUserId: "u-lukas", primaryEmail: "lukas@test.cz");
            s.AddRegistration(1000, submission.Id, lukas.Id, AttendeeType.Adult);
        });

        var result = await Check(seeded, "LUKAS@TEST.CZ", gameId: 1);

        Assert.True(result.IsRegistered);
        Assert.False(result.GuardianOnly);
    }

    // ----- Scenario 6: Whitespace-trimmed input -----
    [Fact]
    public async Task WhitespaceInInput_IsTrimmedBeforeMatching()
    {
        await using var seeded = await SeedAsync(s =>
        {
            var game = s.AddGame(1);
            var lukas = s.AddPerson(100, "Lukáš", "Heinz", 1984, "lukas@test.cz");
            var submission = s.AddSubmission(10, game.Id, registrantUserId: "u-lukas", primaryEmail: "lukas@test.cz");
            s.AddRegistration(1000, submission.Id, lukas.Id, AttendeeType.Adult);
        });

        var result = await Check(seeded, "   lukas@test.cz   ", gameId: 1);

        Assert.True(result.IsRegistered);
        Assert.False(result.GuardianOnly);
    }

    // ----- Scenario 7: Email matches an attendee in a different game only -----
    [Fact]
    public async Task EmailInDifferentGame_ReturnsFalseForThisGame()
    {
        await using var seeded = await SeedAsync(s =>
        {
            var game1 = s.AddGame(1);
            var game2 = s.AddGame(2);
            var lukas = s.AddPerson(100, "Lukáš", "Heinz", 1984, "lukas@test.cz");
            // Registered in game 2 only
            var submission = s.AddSubmission(50, game2.Id, registrantUserId: "u-lukas", primaryEmail: "lukas@test.cz");
            s.AddRegistration(5000, submission.Id, lukas.Id, AttendeeType.Adult);
            _ = game1; // game 1 has no registration for Lukáš
        });

        var result = await Check(seeded, "lukas@test.cz", gameId: 1);

        Assert.False(result.IsRegistered);
        Assert.False(result.GuardianOnly);
    }

    // ----- Scenario 8: Email matches a soft-deleted submission's rows -----
    [Fact]
    public async Task SoftDeletedSubmission_IsIgnored()
    {
        await using var seeded = await SeedAsync(s =>
        {
            var game = s.AddGame(1);
            var lukas = s.AddPerson(100, "Lukáš", "Heinz", 1984, "lukas@test.cz");
            var submission = s.AddSubmission(10, game.Id, registrantUserId: "u-lukas", primaryEmail: "lukas@test.cz");
            submission.IsDeleted = true; // soft-deleted — must be filtered out
            s.AddRegistration(1000, submission.Id, lukas.Id, AttendeeType.Adult);
        });

        var result = await Check(seeded, "lukas@test.cz", gameId: 1);

        Assert.False(result.IsRegistered);
        Assert.False(result.GuardianOnly);
    }

    // ----- Scenario 9: Person.Email null → falls back to ApplicationUser link -----
    [Fact]
    public async Task PersonEmailNull_FallsBackToApplicationUserLink()
    {
        // This is the exact scenario that triggered the production bug: during
        // dedup/import, SubmissionService nulls out Person.Email if it would
        // duplicate the submission's primary. The ApplicationUser.NormalizedEmail
        // is the only remaining link from email → PersonId.
        await using var seeded = await SeedAsync(s =>
        {
            var game = s.AddGame(1);
            var lukas = s.AddPerson(100, "Lukáš", "Heinz", 1984, email: null);
            s.AddApplicationUser("u-lukas", "lukas@test.cz", personId: lukas.Id);
            var submission = s.AddSubmission(10, game.Id, registrantUserId: "u-lukas", primaryEmail: "lukas@test.cz");
            s.AddRegistration(1000, submission.Id, lukas.Id, AttendeeType.Adult);
        });

        var result = await Check(seeded, "lukas@test.cz", gameId: 1);

        Assert.True(result.IsRegistered);
        Assert.False(result.GuardianOnly);
    }

    // ----- Helpers -----

    private static async Task<PresenceCheckDto> Check(SeededDb seeded, string email, int gameId)
    {
        await using var db = seeded.NewContext();
        var userEmailService = new UserEmailService(seeded.Factory, new FixedTimeProvider(FixedUtc));
        return await IntegrationApiEndpoints.CheckRegistrationPresenceAsync(
            db, userEmailService, email, gameId, CancellationToken.None);
    }

    private sealed class FixedTimeProvider(DateTime fixedUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(fixedUtc, TimeSpan.Zero);
    }

    private static async Task<SeededDb> SeedAsync(Action<Seeder> seedAction)
    {
        var dbName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var factory = new InMemoryDbContextFactory(options);

        await using var db = new ApplicationDbContext(options);
        var seeder = new Seeder(db);
        seedAction(seeder);
        await db.SaveChangesAsync();

        return new SeededDb(options, factory);
    }

    private sealed class Seeder(ApplicationDbContext db)
    {
        public Game AddGame(int id)
        {
            var game = new Game
            {
                Id = id,
                Name = $"Ovčina {id}",
                StartsAtUtc = FixedUtc.AddDays(30),
                EndsAtUtc = FixedUtc.AddDays(31),
                RegistrationClosesAtUtc = FixedUtc.AddDays(15),
                MealOrderingClosesAtUtc = FixedUtc.AddDays(10),
                PaymentDueAtUtc = FixedUtc.AddDays(20),
                PlayerBasePrice = 1200,
                AdultHelperBasePrice = 800,
                BankAccount = "123456789/0100",
                BankAccountName = "Ovčina",
                VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc,
                IsPublished = true
            };
            db.Games.Add(game);
            return game;
        }

        public Person AddPerson(int id, string firstName, string lastName, int birthYear, string? email)
        {
            var person = new Person
            {
                Id = id,
                FirstName = firstName,
                LastName = lastName,
                BirthYear = birthYear,
                Email = email,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc
            };
            db.People.Add(person);
            return person;
        }

        public ApplicationUser AddApplicationUser(string id, string email, int? personId = null)
        {
            var user = new ApplicationUser
            {
                Id = id,
                DisplayName = email,
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                EmailConfirmed = true,
                IsActive = true,
                PersonId = personId,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = FixedUtc
            };
            db.Users.Add(user);
            return user;
        }

        public RegistrationSubmission AddSubmission(int id, int gameId, string registrantUserId, string primaryEmail)
        {
            var submission = new RegistrationSubmission
            {
                Id = id,
                GameId = gameId,
                RegistrantUserId = registrantUserId,
                PrimaryContactName = "Test Contact",
                PrimaryEmail = primaryEmail,
                PrimaryPhone = "777111222",
                Status = SubmissionStatus.Submitted,
                SubmittedAtUtc = FixedUtc,
                LastEditedAtUtc = FixedUtc,
                ExpectedTotalAmount = 1200
            };
            db.RegistrationSubmissions.Add(submission);
            return submission;
        }

        public Registration AddRegistration(int id, int submissionId, int personId, AttendeeType type)
        {
            var reg = new Registration
            {
                Id = id,
                SubmissionId = submissionId,
                PersonId = personId,
                AttendeeType = type,
                Status = RegistrationStatus.Active,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc
            };
            db.Registrations.Add(reg);
            return reg;
        }
    }

    private sealed class SeededDb(DbContextOptions<ApplicationDbContext> options, IDbContextFactory<ApplicationDbContext> factory)
        : IAsyncDisposable
    {
        public IDbContextFactory<ApplicationDbContext> Factory => factory;
        public ApplicationDbContext NewContext() => new(options);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InMemoryDbContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }
}
