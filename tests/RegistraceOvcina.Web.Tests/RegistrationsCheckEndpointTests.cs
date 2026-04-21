using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Integration;

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
///  14. Submission.PrimaryContactName matches an Adult attendee's First+LastName
///      with no ApplicationUser or Person.Email → not guardian-only (Lukáš case).
///  15. Same but the matched attendee is a Player (child) → stays guardian-only.
///  16. PrimaryContactName without diacritics matches Person name with diacritics
///      (Lukas Heinz vs Lukáš Heinz) → not guardian-only.
///  17. PrimaryContactName does not match any attendee → stays guardian-only.
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

    // ----- Scenario 10: Person.Email null, ApplicationUser primary email match,
    // Submission.PrimaryEmail also matches, registration exists → NotGuardianOnly. -----
    [Fact]
    public async Task PersonEmailNulled_UserEmailLinked_WithOwnRegistration_ReturnsNotGuardianOnly()
    {
        // Targeted regression for the PrimaryEmail-wins-over-AppUserLink bug:
        // Person.Email is null (dedup nulled it out), ApplicationUser carries the
        // email and is linked via PersonId, and the attendee has their own active
        // Registration in the game. The Submission.PrimaryEmail also matches
        // (common for self-registering adults). The own-Registration signal MUST
        // override the PrimaryEmail-only verdict.
        await using var seeded = await SeedAsync(s =>
        {
            var game = s.AddGame(1);
            var person = s.AddPerson(346, "Lukáš", "Heinz", 1984, email: null);
            s.AddApplicationUser("u-346", "lukas.heinz@seznam.cz", personId: person.Id);
            var submission = s.AddSubmission(1000, game.Id, registrantUserId: "u-346", primaryEmail: "lukas.heinz@seznam.cz");
            s.AddRegistration(1058, submission.Id, person.Id, AttendeeType.Adult);
        });

        var result = await Check(seeded, "lukas.heinz@seznam.cz", gameId: 1);

        Assert.True(result.IsRegistered);
        Assert.False(result.GuardianOnly);
    }

    // ----- Scenario 11: Alternate UserEmails row (not the primary) linked to a
    // Person who has their own Registration → NotGuardianOnly. -----
    [Fact]
    public async Task AlternateUserEmail_LinkedToPerson_WithOwnRegistration_ReturnsNotGuardianOnly()
    {
        // User's ApplicationUser.NormalizedEmail is a different address; the
        // incoming email only matches an entry in UserEmails (alias). The link
        // to Person still resolves via ApplicationUser.PersonId, and that Person
        // has an active Adult Registration in this game. Must be NotGuardianOnly.
        await using var seeded = await SeedAsync(s =>
        {
            var game = s.AddGame(1);
            var person = s.AddPerson(400, "Alt", "User", 1990, email: null);
            s.AddApplicationUser("u-alt", "primary@test.cz", personId: person.Id);
            s.AddUserEmail("u-alt", "alias@test.cz");
            var submission = s.AddSubmission(40, game.Id, registrantUserId: "u-alt", primaryEmail: "primary@test.cz");
            s.AddRegistration(4000, submission.Id, person.Id, AttendeeType.Adult);
        });

        var result = await Check(seeded, "alias@test.cz", gameId: 1);

        Assert.True(result.IsRegistered);
        Assert.False(result.GuardianOnly);
    }

    // ----- Scenario 12: ApplicationUser links resolve email → Person, but that
    // Person has NO Registration in the game. Only the submission's PrimaryEmail
    // matches. → Stays guardianOnly=true. -----
    [Fact]
    public async Task UserLinkedButNoOwnRegistrationInGame_OnlyPrimaryContactMatch_StaysGuardianOnly()
    {
        // The classic guardian case: parent is the registrant + Submission's
        // primary contact, has an ApplicationUser linked to a Person, but did
        // not register themselves as an attendee — only their kids. The endpoint
        // must keep guardianOnly=true here.
        await using var seeded = await SeedAsync(s =>
        {
            var game = s.AddGame(1);
            var parent = s.AddPerson(500, "Marek", "Štěpán", 1980, email: null);
            s.AddApplicationUser("u-marek", "marek@test.cz", personId: parent.Id);
            var kid = s.AddPerson(501, "Anna", "Štěpán", 2014, email: null);
            var submission = s.AddSubmission(50, game.Id, registrantUserId: "u-marek", primaryEmail: "marek@test.cz");
            s.AddRegistration(5001, submission.Id, kid.Id, AttendeeType.Player);
            // Note: no Registration for parent.Id — only a kid.
        });

        var result = await Check(seeded, "marek@test.cz", gameId: 1);

        Assert.True(result.IsRegistered);
        Assert.True(result.GuardianOnly);
    }

    // ----- Scenario 13: Full Lukáš production snapshot. -----
    [Fact]
    public async Task LukasCaseSnapshot()
    {
        // Full reproduction of the production scenario that triggered this fix:
        //   - Person.Email = null (dedup path).
        //   - ApplicationUser linked to Person with Email populated.
        //   - An active Adult Registration exists in the game.
        //   - Submission.PrimaryEmail matches the same email (self-registrant).
        // Expected: isRegistered=true, guardianOnly=false.
        await using var seeded = await SeedAsync(s =>
        {
            var game = s.AddGame(1);
            var lukas = s.AddPerson(346, "Lukáš", "Heinz", 1984, email: null);
            s.AddApplicationUser("u-lukas-prod", "lukas.heinz@seznam.cz", personId: lukas.Id);
            var submission = s.AddSubmission(1000, game.Id, registrantUserId: "u-lukas-prod", primaryEmail: "lukas.heinz@seznam.cz");
            s.AddRegistration(1058, submission.Id, lukas.Id, AttendeeType.Adult);
        });

        var result = await Check(seeded, "lukas.heinz@seznam.cz", gameId: 1);

        Assert.True(result.IsRegistered);
        Assert.False(result.GuardianOnly);
    }

    // ----- Scenario 14: Primary-contact-name matches an Adult attendee -----
    // The real-data Lukáš Heinz case: Person.Email is null (dedup),
    // no ApplicationUser links the email to PersonId, BUT the submission's
    // PrimaryContactName matches an Adult attendee's First+LastName in the
    // same submission. That should count as an own-registration, not guardian.
    [Fact]
    public async Task PrimaryContactNameMatchesAdultAttendee_ReturnsNotGuardianOnly()
    {
        await using var seeded = await SeedAsync(s =>
        {
            var game = s.AddGame(1);
            var lukas = s.AddPerson(346, "Lukáš", "Heinz", 1984, email: null);
            // No ApplicationUser, no UserEmails — the ONLY email signal
            // is Submission.PrimaryEmail + PrimaryContactName.
            var submission = s.AddSubmission(
                id: 1000,
                gameId: game.Id,
                registrantUserId: "u-lukas",
                primaryEmail: "lukas.heinz@seznam.cz",
                primaryContactName: "Lukáš Heinz");
            s.AddRegistration(1058, submission.Id, lukas.Id, AttendeeType.Adult);
        });

        var result = await Check(seeded, "lukas.heinz@seznam.cz", gameId: 1);

        Assert.True(result.IsRegistered);
        Assert.False(result.GuardianOnly);
    }

    // ----- Scenario 15: Primary-contact-name matches a Player (child), not adult -----
    // A parent who happens to share a name with their child (or a typo) must
    // NOT flip the guardianOnly flag — only Adult attendees qualify for this
    // signal. The submission has the matching name, but AttendeeType=Player.
    [Fact]
    public async Task PrimaryContactNameMatchesNonAdultAttendee_StaysGuardianOnly()
    {
        await using var seeded = await SeedAsync(s =>
        {
            var game = s.AddGame(1);
            var kidWithSameName = s.AddPerson(346, "Lukáš", "Heinz", 2014, email: null);
            var submission = s.AddSubmission(
                id: 1000,
                gameId: game.Id,
                registrantUserId: "u-lukas",
                primaryEmail: "lukas.heinz@seznam.cz",
                primaryContactName: "Lukáš Heinz");
            s.AddRegistration(1058, submission.Id, kidWithSameName.Id, AttendeeType.Player);
        });

        var result = await Check(seeded, "lukas.heinz@seznam.cz", gameId: 1);

        Assert.True(result.IsRegistered);
        Assert.True(result.GuardianOnly);
    }

    // ----- Scenario 16: PrimaryContactName without diacritics matches Person with diacritics -----
    // Primary contact typed "Lukas Heinz" (ASCII, no diacritics); the Person
    // record has FirstName="Lukáš". The comparison is diacritic-normalized, so
    // this must count as an adult attendee match.
    [Fact]
    public async Task PrimaryContactNameMatchWithDiacritics_Works()
    {
        await using var seeded = await SeedAsync(s =>
        {
            var game = s.AddGame(1);
            var lukas = s.AddPerson(346, "Lukáš", "Heinz", 1984, email: null);
            var submission = s.AddSubmission(
                id: 1000,
                gameId: game.Id,
                registrantUserId: "u-lukas",
                primaryEmail: "lukas.heinz@seznam.cz",
                primaryContactName: "Lukas Heinz");
            s.AddRegistration(1058, submission.Id, lukas.Id, AttendeeType.Adult);
        });

        var result = await Check(seeded, "lukas.heinz@seznam.cz", gameId: 1);

        Assert.True(result.IsRegistered);
        Assert.False(result.GuardianOnly);
    }

    // ----- Scenario 17: PrimaryContactName does NOT match any attendee -----
    // Submission's PrimaryEmail matches but the PrimaryContactName is different
    // from every attendee's name (and the attendees are Players — kids). Must
    // stay guardianOnly=true to avoid over-matching. E.g., Klára Heinzová
    // should not be treated as Lukáš Heinz's own-registration.
    [Fact]
    public async Task PrimaryContactNameNoMatch_SubmissionOnly_StaysGuardianOnly()
    {
        await using var seeded = await SeedAsync(s =>
        {
            var game = s.AddGame(1);
            // A completely different person (different first AND last name)
            // is the only Adult attendee in the submission. Primary contact
            // name "Lukáš Heinz" does not match this adult.
            var other = s.AddPerson(346, "Klára", "Heinzová", 1984, email: null);
            var submission = s.AddSubmission(
                id: 1000,
                gameId: game.Id,
                registrantUserId: "u-lukas",
                primaryEmail: "lukas.heinz@seznam.cz",
                primaryContactName: "Lukáš Heinz");
            s.AddRegistration(1058, submission.Id, other.Id, AttendeeType.Adult);
        });

        var result = await Check(seeded, "lukas.heinz@seznam.cz", gameId: 1);

        Assert.True(result.IsRegistered);
        Assert.True(result.GuardianOnly);
    }

    // ----- Helpers -----

    private static async Task<PresenceCheckDto> Check(SeededDb seeded, string email, int gameId)
    {
        await using var db = seeded.NewContext();
        return await IntegrationApiEndpoints.CheckRegistrationPresenceAsync(
            db, email, gameId, CancellationToken.None);
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

        public UserEmail AddUserEmail(string userId, string email)
        {
            var entity = new UserEmail
            {
                UserId = userId,
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                CreatedAtUtc = FixedUtc
            };
            db.UserEmails.Add(entity);
            return entity;
        }

        public RegistrationSubmission AddSubmission(
            int id,
            int gameId,
            string registrantUserId,
            string primaryEmail,
            string primaryContactName = "Test Contact")
        {
            var submission = new RegistrationSubmission
            {
                Id = id,
                GameId = gameId,
                RegistrantUserId = registrantUserId,
                PrimaryContactName = primaryContactName,
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
