using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Submissions;

namespace RegistraceOvcina.Web.Tests;

public sealed class SubmissionServiceStaffOverrideTests
{
    private static readonly DateTime FixedUtc = new(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetSubmissionAsync_StaffAfterDeadline_CanEditButNotAddAttendees()
    {
        var (options, game, submission, _, user) = await SeedPastDeadlineScenario();

        var service = CreateService(options);

        var vm = await service.GetSubmissionAsync(submission.Id, user.Id, isStaff: true);

        Assert.NotNull(vm);
        Assert.True(vm.CanEditRegistration);
        Assert.True(vm.CanEditMeals);
        Assert.False(vm.CanAddAttendees);
        Assert.True(vm.IsStaffView);
    }

    [Fact]
    public async Task GetSubmissionAsync_RegularUserAfterDeadline_CannotEdit()
    {
        var (options, _, submission, _, _) = await SeedPastDeadlineScenario();

        var service = CreateService(options);

        // Use the registrant's own userId — they own the submission but
        // the deadline has passed, so everything should be locked.
        var vm = await service.GetSubmissionAsync(submission.Id, "registrant-user-id", isStaff: false);

        Assert.NotNull(vm);
        Assert.False(vm.CanEditRegistration);
        Assert.False(vm.CanEditMeals);
        Assert.False(vm.CanAddAttendees);
        Assert.False(vm.IsStaffView);
    }

    [Fact]
    public async Task AddAttendeeAsync_BlockedForStaffAfterDeadline()
    {
        var (options, _, submission, _, user) = await SeedPastDeadlineScenario();

        var service = CreateService(options);

        var input = new AttendeeInput
        {
            FirstName = "Nový",
            LastName = "Účastník",
            BirthYear = 2015,
            AttendeeType = AttendeeType.Player,
            PlayerSubType = PlayerSubType.Pvp
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => service.AddAttendeeAsync(submission.Id, user.Id, input, isStaff: true));

        Assert.Contains("uzavřená", ex.Message);
    }

    [Fact]
    public async Task UpdateAttendeeAsync_AllowedForStaffAfterDeadline()
    {
        var (options, _, submission, registration, user) = await SeedPastDeadlineScenario();

        var service = CreateService(options);

        var input = new AttendeeInput
        {
            FirstName = registration.Person.FirstName,
            LastName = registration.Person.LastName,
            BirthYear = registration.Person.BirthYear,
            AttendeeType = registration.AttendeeType,
            PlayerSubType = registration.PlayerSubType,
            LodgingPreference = LodgingPreference.OwnTent,
            AttendeeNote = "Změněno organizátorem po uzávěrce",
            GuardianName = "Jana Nováková",
            GuardianRelationship = "matka",
            GuardianAuthorizationConfirmed = true
        };

        await service.UpdateAttendeeAsync(
            submission.Id, registration.Id, user.Id, input, isStaff: true);

        await using var db = new ApplicationDbContext(options);
        var updated = await db.Registrations.Include(r => r.Person)
            .SingleAsync(r => r.Id == registration.Id);

        Assert.Equal(LodgingPreference.OwnTent, updated.LodgingPreference);
        Assert.Equal("Změněno organizátorem po uzávěrce", updated.RegistrantNote);
    }

    [Fact]
    public async Task GetSubmissionAsync_StaffCanOpenAnySubmission()
    {
        var (options, _, submission, _, _) = await SeedPastDeadlineScenario();

        var service = CreateService(options);

        // A completely different user id — staff override should still load
        var vm = await service.GetSubmissionAsync(submission.Id, "unrelated-user-id", isStaff: true);

        Assert.NotNull(vm);
        Assert.True(vm.IsStaffView);
    }

    [Fact]
    public async Task GetSubmissionAsync_RegularUserCannotOpenOthersSubmission()
    {
        var (options, _, submission, _, _) = await SeedPastDeadlineScenario();

        var service = CreateService(options);

        var vm = await service.GetSubmissionAsync(submission.Id, "unrelated-user-id", isStaff: false);

        Assert.Null(vm);
    }

    // --- Infrastructure ---

    private static SubmissionService CreateService(DbContextOptions<ApplicationDbContext> options)
    {
        var timeProvider = new FixedTimeProvider();
        var pricingService = new SubmissionPricingService(timeProvider);
        return new SubmissionService(
            new TestDbContextFactory(options),
            pricingService,
            timeProvider);
    }

    private static async Task<(
        DbContextOptions<ApplicationDbContext> Options,
        Game Game,
        RegistrationSubmission Submission,
        Registration Registration,
        ApplicationUser User)>
        SeedPastDeadlineScenario()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        // Game whose registration AND meal deadline are in the past relative to FixedUtc
        var game = new Game
        {
            Id = 1,
            Name = "Ovčina 2026",
            StartsAtUtc = FixedUtc.AddDays(-5),
            EndsAtUtc = FixedUtc.AddDays(-4),
            RegistrationClosesAtUtc = FixedUtc.AddDays(-10),
            MealOrderingClosesAtUtc = FixedUtc.AddDays(-8),
            PaymentDueAtUtc = FixedUtc.AddDays(-7),
            PlayerBasePrice = 1200,
            AdultHelperBasePrice = 800,
            BankAccount = "123/0100",
            BankAccountName = "Ovčina",
            VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
            CreatedAtUtc = FixedUtc.AddDays(-30),
            UpdatedAtUtc = FixedUtc.AddDays(-30),
            IsPublished = true
        };

        var user = new ApplicationUser
        {
            Id = "staff-user-id",
            DisplayName = "Organizátor",
            Email = "org@example.cz",
            NormalizedEmail = "ORG@EXAMPLE.CZ",
            UserName = "org@example.cz",
            NormalizedUserName = "ORG@EXAMPLE.CZ",
            EmailConfirmed = true,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = FixedUtc.AddDays(-30)
        };

        // Submission owned by a DIFFERENT user than `user` — so staff override is meaningful
        var registrantUser = new ApplicationUser
        {
            Id = "registrant-user-id",
            DisplayName = "Rodič",
            Email = "rodic@example.cz",
            NormalizedEmail = "RODIC@EXAMPLE.CZ",
            UserName = "rodic@example.cz",
            NormalizedUserName = "RODIC@EXAMPLE.CZ",
            EmailConfirmed = true,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = FixedUtc.AddDays(-30)
        };

        var person = new Person
        {
            Id = 1,
            FirstName = "Kuba",
            LastName = "Novák",
            BirthYear = 2015,
            CreatedAtUtc = FixedUtc.AddDays(-20),
            UpdatedAtUtc = FixedUtc.AddDays(-20)
        };

        var submission = new RegistrationSubmission
        {
            Id = 1,
            GameId = game.Id,
            RegistrantUserId = registrantUser.Id,
            PrimaryContactName = "Jana Nováková",
            PrimaryEmail = "jana@example.cz",
            PrimaryPhone = "777111222",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc.AddDays(-15),
            LastEditedAtUtc = FixedUtc.AddDays(-15),
            ExpectedTotalAmount = 1200
        };

        var registration = new Registration
        {
            Id = 1,
            SubmissionId = submission.Id,
            PersonId = person.Id,
            AttendeeType = AttendeeType.Player,
            PlayerSubType = PlayerSubType.Pvp,
            Status = RegistrationStatus.Active,
            LodgingPreference = LodgingPreference.Indoor,
            CreatedAtUtc = FixedUtc.AddDays(-15),
            UpdatedAtUtc = FixedUtc.AddDays(-15)
        };

        await using (var db = new ApplicationDbContext(options))
        {
            db.Games.Add(game);
            db.Users.AddRange(user, registrantUser);
            db.People.Add(person);
            db.RegistrationSubmissions.Add(submission);
            db.Registrations.Add(registration);
            await db.SaveChangesAsync();
        }

        return (options, game, submission, registration, user);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ApplicationDbContext(options));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now = new(FixedUtc);
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
