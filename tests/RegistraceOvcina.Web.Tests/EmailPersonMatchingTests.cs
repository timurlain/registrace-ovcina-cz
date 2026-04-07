using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Submissions;

namespace RegistraceOvcina.Web.Tests;

public sealed class EmailPersonMatchingTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc);
    private const string UserId = "test-user-1";

    [Fact]
    public async Task AddAttendee_SameName_SameEmail_ReusesExistingPerson()
    {
        // Arrange
        var options = CreateOptions();
        int existingPersonId;
        int submissionId;

        await using (var db = new ApplicationDbContext(options))
        {
            var person = CreatePerson("Jan", "Novák", 1990, "jan@example.com");
            db.People.Add(person);
            await db.SaveChangesAsync();
            existingPersonId = person.Id;

            var game = CreateGame();
            db.Games.Add(game);
            var user = CreateUser();
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var submission = CreateSubmission(game.Id);
            db.RegistrationSubmissions.Add(submission);
            await db.SaveChangesAsync();
            submissionId = submission.Id;
        }

        var service = CreateService(options);

        var input = new AttendeeInput
        {
            FirstName = "Jan",
            LastName = "Novák",
            BirthYear = 1991,
            AttendeeType = AttendeeType.Player,
            PlayerSubType = PlayerSubType.Pvp,
            ContactEmail = "jan@example.com"
        };

        // Act
        await service.AddAttendeeAsync(submissionId, UserId, input);

        // Assert
        await using (var db = new ApplicationDbContext(options))
        {
            var people = await db.People.ToListAsync();
            Assert.Single(people);
            Assert.Equal(existingPersonId, people[0].Id);
            Assert.Equal(1991, people[0].BirthYear); // birth year updated

            var registrations = await db.Registrations.ToListAsync();
            Assert.Single(registrations);
            Assert.Equal(existingPersonId, registrations[0].PersonId);
        }
    }

    [Fact]
    public async Task AddAttendee_DifferentName_SameEmail_ThrowsEmailConflict()
    {
        // Arrange
        var options = CreateOptions();
        int submissionId;

        await using (var db = new ApplicationDbContext(options))
        {
            var person = CreatePerson("Jan", "Novák", 1990, "jan@example.com");
            db.People.Add(person);
            var game = CreateGame();
            db.Games.Add(game);
            var user = CreateUser();
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var submission = CreateSubmission(game.Id);
            db.RegistrationSubmissions.Add(submission);
            await db.SaveChangesAsync();
            submissionId = submission.Id;
        }

        var service = CreateService(options);

        var input = new AttendeeInput
        {
            FirstName = "Petr",
            LastName = "Svoboda",
            BirthYear = 1985,
            AttendeeType = AttendeeType.Player,
            PlayerSubType = PlayerSubType.Pvp,
            ContactEmail = "jan@example.com"
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<EmailConflictException>(
            () => service.AddAttendeeAsync(submissionId, UserId, input));

        Assert.Equal("Jan", ex.ExistingFirstName);
        Assert.Equal("Novák", ex.ExistingLastName);
        Assert.Equal("jan@example.com", ex.ConflictEmail);
    }

    [Fact]
    public async Task AddAttendee_DifferentName_UseExistingPersonId_ReusesAndUpdates()
    {
        // Arrange
        var options = CreateOptions();
        int existingPersonId;
        int submissionId;

        await using (var db = new ApplicationDbContext(options))
        {
            var person = CreatePerson("Jan", "Novák", 1990, "jan@example.com");
            db.People.Add(person);
            var game = CreateGame();
            db.Games.Add(game);
            var user = CreateUser();
            db.Users.Add(user);
            await db.SaveChangesAsync();
            existingPersonId = person.Id;

            var submission = CreateSubmission(game.Id);
            db.RegistrationSubmissions.Add(submission);
            await db.SaveChangesAsync();
            submissionId = submission.Id;
        }

        var service = CreateService(options);

        var input = new AttendeeInput
        {
            FirstName = "Petr",
            LastName = "Svoboda",
            BirthYear = 2000,
            AttendeeType = AttendeeType.Player,
            PlayerSubType = PlayerSubType.Pvp,
            ContactEmail = "jan@example.com",
            UseExistingPersonId = existingPersonId
        };

        // Act
        await service.AddAttendeeAsync(submissionId, UserId, input);

        // Assert
        await using (var db = new ApplicationDbContext(options))
        {
            var people = await db.People.ToListAsync();
            Assert.Single(people);
            Assert.Equal(existingPersonId, people[0].Id);
            Assert.Equal(2000, people[0].BirthYear); // birth year updated
            // Name stays as original person's name (UseExistingPersonId reuses the person as-is)
            Assert.Equal("Jan", people[0].FirstName);

            var registrations = await db.Registrations.ToListAsync();
            Assert.Single(registrations);
            Assert.Equal(existingPersonId, registrations[0].PersonId);
        }
    }

    [Fact]
    public async Task AddAttendee_ClearEmail_SavesWithoutEmail()
    {
        // Arrange
        var options = CreateOptions();
        int submissionId;

        await using (var db = new ApplicationDbContext(options))
        {
            var game = CreateGame();
            db.Games.Add(game);
            var user = CreateUser();
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var submission = CreateSubmission(game.Id);
            db.RegistrationSubmissions.Add(submission);
            await db.SaveChangesAsync();
            submissionId = submission.Id;
        }

        var service = CreateService(options);

        var input = new AttendeeInput
        {
            FirstName = "Jana",
            LastName = "Nová",
            BirthYear = 1995,
            AttendeeType = AttendeeType.Player,
            PlayerSubType = PlayerSubType.Pvp,
            ContactEmail = "jana@example.com",
            ClearEmail = true
        };

        // Act
        await service.AddAttendeeAsync(submissionId, UserId, input);

        // Assert
        await using (var db = new ApplicationDbContext(options))
        {
            var person = await db.People.SingleAsync();
            Assert.Null(person.Email);
        }
    }

    // --- Helpers ---

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static SubmissionService CreateService(DbContextOptions<ApplicationDbContext> options) =>
        new(new TestDbContextFactory(options),
            new SubmissionPricingService(new FixedTimeProvider()),
            new FixedTimeProvider());

    private static Person CreatePerson(string firstName, string lastName, int birthYear, string? email) =>
        new()
        {
            FirstName = firstName,
            LastName = lastName,
            BirthYear = birthYear,
            Email = email,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        };

    private static Game CreateGame() =>
        new()
        {
            Name = "Ovčina XXVI",
            StartsAtUtc = FixedUtc.AddMonths(2),
            EndsAtUtc = FixedUtc.AddMonths(2).AddDays(3),
            RegistrationClosesAtUtc = FixedUtc.AddMonths(1),
            MealOrderingClosesAtUtc = FixedUtc.AddMonths(1),
            PaymentDueAtUtc = FixedUtc.AddMonths(1),
            PlayerBasePrice = 1500m,
            AdultHelperBasePrice = 0m,
            BankAccount = "123456789/0100",
            BankAccountName = "Ovčina z.s.",
            IsPublished = true,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        };

    private static ApplicationUser CreateUser() =>
        new()
        {
            Id = UserId,
            UserName = "testuser@example.com",
            Email = "testuser@example.com",
            DisplayName = "Test User",
            NormalizedUserName = "TESTUSER@EXAMPLE.COM",
            NormalizedEmail = "TESTUSER@EXAMPLE.COM"
        };

    private static RegistrationSubmission CreateSubmission(int gameId) =>
        new()
        {
            GameId = gameId,
            RegistrantUserId = UserId,
            PrimaryContactName = "Test User",
            PrimaryEmail = "testuser@example.com",
            PrimaryPhone = "777111222",
            Status = SubmissionStatus.Draft,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 0m
        };

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now = new(FixedUtc);

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
