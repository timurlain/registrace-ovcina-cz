using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RegistraceOvcina.Web.Components.Pages.Organizer;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.People;
using RegistraceOvcina.Web.Security;

namespace RegistraceOvcina.Web.Tests;

public sealed class PeopleReviewServiceTests
{
    [Fact]
    public void PeoplePage_RequiresStaffPolicy()
    {
        var authorizeAttribute = typeof(People)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();

        Assert.Equal(AuthorizationPolicies.StaffOnly, authorizeAttribute.Policy);
    }

    [Fact]
    public void PersonDetailPage_RequiresStaffPolicy()
    {
        var authorizeAttribute = typeof(PersonDetail)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();

        Assert.Equal(AuthorizationPolicies.StaffOnly, authorizeAttribute.Policy);
    }

    [Fact]
    public async Task GetDetailAsync_ReturnsDuplicateCandidatesAndLinkableAccounts()
    {
        var options = CreateOptions();
        var person = CreatePerson(1, "Jana", "Nováková", 2012, "jana@example.cz", "777 111 222");
        var duplicate = CreatePerson(2, "Jana", "Nováková", 2012, "jana@example.cz", "777111222");
        var registrant = CreateUser("registrant-id", "registrant@example.cz");
        var linkableUser = CreateUser("user-id", "jana@example.cz");
        var game = CreateGame(1, "Ovčina 2026", new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc));
        var submission = CreateSubmission(1, game.Id, registrant.Id, "Jana Nováková");

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.AddRange(registrant, linkableUser);
            db.Games.Add(game);
            db.RegistrationSubmissions.Add(submission);
            db.People.AddRange(person, duplicate);
            db.Registrations.Add(new Registration
            {
                Id = 1,
                SubmissionId = submission.Id,
                PersonId = duplicate.Id,
                AttendeeType = AttendeeType.Player,
                Status = RegistrationStatus.Active,
                CharacterName = "Lískulka",
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc
            });
            await db.SaveChangesAsync();
        }

        var service = new PeopleReviewService(new TestDbContextFactory(options), new FixedTimeProvider());

        var detail = await service.GetDetailAsync(person.Id);

        Assert.NotNull(detail);
        Assert.Single(detail.LinkableAccounts);
        Assert.Collection(
            detail.MatchCandidates,
            candidate =>
            {
                Assert.Equal(duplicate.Id, candidate.Id);
                Assert.Contains("Stejné jméno a ročník", candidate.MatchReasons);
                Assert.Contains("Stejný e-mail", candidate.MatchReasons);
                Assert.Contains("Stejný telefon", candidate.MatchReasons);
            });
    }

    [Fact]
    public async Task LinkUserAsync_LinksAccountAndWritesAuditLog()
    {
        var options = CreateOptions();
        var person = CreatePerson(1, "Klára", "Bílá", 2010, "klara@example.cz", null);
        var actor = CreateUser("actor-id", "admin@example.cz");
        var target = CreateUser("target-id", "klara@example.cz");

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.AddRange(actor, target);
            db.People.Add(person);
            await db.SaveChangesAsync();
        }

        var service = new PeopleReviewService(new TestDbContextFactory(options), new FixedTimeProvider());

        await service.LinkUserAsync(person.Id, target.Id, actor.Id);

        await using var verificationDb = new ApplicationDbContext(options);
        var reloadedUser = await verificationDb.Users.SingleAsync(x => x.Id == target.Id);
        Assert.Equal(person.Id, reloadedUser.PersonId);

        var audit = await verificationDb.AuditLogs.SingleAsync();
        Assert.Equal("PersonUserLinked", audit.Action);
        Assert.Equal(person.Id.ToString(), audit.EntityId);
    }

    [Fact]
    public async Task MergeAsync_MovesReferencesToCanonicalPersonAndSoftDeletesDuplicate()
    {
        var options = CreateOptions();
        var actor = CreateUser("actor-id", "admin@example.cz");
        var registrant = CreateUser("registrant-id", "registrant@example.cz");
        var linkedUser = CreateUser("linked-id", "duplicate@example.cz", personId: 2);
        var canonical = CreatePerson(1, "Tomáš", "Kopecký", 2011, null, null, "Stará poznámka");
        var duplicate = CreatePerson(2, "Tomáš", "Kopecký", 2011, "duplicate@example.cz", "603 123 456", "Nová poznámka");
        var game = CreateGame(1, "Ovčina 2026", new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc));
        var submission = CreateSubmission(1, game.Id, registrant.Id, "Magda Kopecká");
        var registration = new Registration
        {
            Id = 1,
            SubmissionId = submission.Id,
            PersonId = duplicate.Id,
            AttendeeType = AttendeeType.Player,
            Status = RegistrationStatus.Active,
            CharacterName = "Arnor",
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        };
        var character = new Character
        {
            Id = 1,
            PersonId = duplicate.Id,
            Name = "Arnor"
        };
        var appearance = new CharacterAppearance
        {
            Id = 1,
            CharacterId = character.Id,
            GameId = game.Id,
            RegistrationId = registration.Id,
            ContinuityStatus = ContinuityStatus.Continued
        };
        var note = new OrganizerNote
        {
            Id = 1,
            PersonId = duplicate.Id,
            AuthorUserId = actor.Id,
            Note = "Pozor na alergii.",
            CreatedAtUtc = FixedUtc
        };
        var message = new EmailMessage
        {
            Id = 1,
            MailboxItemId = "mail-1",
            Direction = EmailDirection.Inbound,
            From = "parent@example.cz",
            To = "registrace@example.cz",
            Subject = "Dotaz",
            LinkedPersonId = duplicate.Id,
            ReceivedAtUtc = FixedUtc
        };
        var importRow = new HistoricalImportRow
        {
            Id = 1,
            SourceFormat = "legacy",
            SourceSheet = "Deti",
            SourceKey = "row-1",
            SourceLabel = "Tomáš Kopecký",
            LinkedPersonId = duplicate.Id,
            FirstImportedAtUtc = FixedUtc,
            LastImportedAtUtc = FixedUtc
        };

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.AddRange(actor, registrant, linkedUser);
            db.Games.Add(game);
            db.RegistrationSubmissions.Add(submission);
            db.People.AddRange(canonical, duplicate);
            db.Registrations.Add(registration);
            db.Characters.Add(character);
            db.CharacterAppearances.Add(appearance);
            db.OrganizerNotes.Add(note);
            db.EmailMessages.Add(message);
            db.HistoricalImportRows.Add(importRow);
            await db.SaveChangesAsync();
        }

        var service = new PeopleReviewService(new TestDbContextFactory(options), new FixedTimeProvider());

        await service.MergeAsync(canonical.Id, duplicate.Id, actor.Id);

        await using var verificationDb = new ApplicationDbContext(options);
        var reloadedRegistration = await verificationDb.Registrations.SingleAsync();
        var reloadedCharacter = await verificationDb.Characters.SingleAsync();
        var reloadedNote = await verificationDb.OrganizerNotes.SingleAsync();
        var reloadedMessage = await verificationDb.EmailMessages.SingleAsync();
        var reloadedImportRow = await verificationDb.HistoricalImportRows.SingleAsync();
        var reloadedUser = await verificationDb.Users.SingleAsync(x => x.Id == linkedUser.Id);
        var reloadedCanonical = await verificationDb.People.SingleAsync(x => x.Id == canonical.Id);
        var deletedDuplicate = await verificationDb.People.IgnoreQueryFilters().SingleAsync(x => x.Id == duplicate.Id);
        var audit = await verificationDb.AuditLogs.SingleAsync();

        Assert.Equal(canonical.Id, reloadedRegistration.PersonId);
        Assert.Equal(canonical.Id, reloadedCharacter.PersonId);
        Assert.Equal(canonical.Id, reloadedNote.PersonId);
        Assert.Equal(canonical.Id, reloadedMessage.LinkedPersonId);
        Assert.Equal(canonical.Id, reloadedImportRow.LinkedPersonId);
        Assert.Equal(canonical.Id, reloadedUser.PersonId);
        Assert.Equal("duplicate@example.cz", reloadedCanonical.Email);
        Assert.Equal("603 123 456", reloadedCanonical.Phone);
        Assert.Contains("Stará poznámka", reloadedCanonical.Notes);
        Assert.Contains("Nová poznámka", reloadedCanonical.Notes);
        Assert.True(deletedDuplicate.IsDeleted);
        Assert.Equal("PersonMerged", audit.Action);
    }

    [Fact]
    public async Task MergeAsync_BlocksWhenBothPeopleAlreadyHaveSameSubmission()
    {
        var options = CreateOptions();
        var actor = CreateUser("actor-id", "admin@example.cz");
        var registrant = CreateUser("registrant-id", "registrant@example.cz");
        var canonical = CreatePerson(1, "Eva", "Novotná", 2013, null, null);
        var duplicate = CreatePerson(2, "Eva", "Novotná", 2013, null, null);
        var game = CreateGame(1, "Ovčina 2026", new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc));
        var submission = CreateSubmission(1, game.Id, registrant.Id, "Eva Novotná");

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.AddRange(actor, registrant);
            db.Games.Add(game);
            db.RegistrationSubmissions.Add(submission);
            db.People.AddRange(canonical, duplicate);
            db.Registrations.AddRange(
                new Registration
                {
                    Id = 1,
                    SubmissionId = submission.Id,
                    PersonId = canonical.Id,
                    AttendeeType = AttendeeType.Player,
                    Status = RegistrationStatus.Active,
                    CreatedAtUtc = FixedUtc,
                    UpdatedAtUtc = FixedUtc
                },
                new Registration
                {
                    Id = 2,
                    SubmissionId = submission.Id,
                    PersonId = duplicate.Id,
                    AttendeeType = AttendeeType.Adult,
                    Status = RegistrationStatus.Active,
                    CreatedAtUtc = FixedUtc,
                    UpdatedAtUtc = FixedUtc
                });
            await db.SaveChangesAsync();
        }

        var service = new PeopleReviewService(new TestDbContextFactory(options), new FixedTimeProvider());

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.MergeAsync(canonical.Id, duplicate.Id, actor.Id));

        Assert.Equal("Obě osoby už mají účast ve stejné přihlášce. Sloučení by nebylo bezpečné.", ex.Message);
    }

    private static readonly DateTime FixedUtc = new(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc);

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static Person CreatePerson(
        int id,
        string firstName,
        string lastName,
        int birthYear,
        string? email,
        string? phone,
        string? notes = null) =>
        new()
        {
            Id = id,
            FirstName = firstName,
            LastName = lastName,
            BirthYear = birthYear,
            Email = email,
            Phone = phone,
            Notes = notes,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        };

    private static Game CreateGame(int id, string name, DateTime startsAtUtc) =>
        new()
        {
            Id = id,
            Name = name,
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = startsAtUtc.AddDays(1),
            RegistrationClosesAtUtc = startsAtUtc.AddDays(-7),
            MealOrderingClosesAtUtc = startsAtUtc.AddDays(-10),
            PaymentDueAtUtc = startsAtUtc.AddDays(-5),
            PlayerBasePrice = 1200,
            AdultHelperBasePrice = 800,
            BankAccount = "123456789/0100",
            BankAccountName = "Ovčina",
            VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc,
            IsPublished = true
        };

    private static RegistrationSubmission CreateSubmission(int id, int gameId, string registrantUserId, string primaryContactName) =>
        new()
        {
            Id = id,
            GameId = gameId,
            RegistrantUserId = registrantUserId,
            PrimaryContactName = primaryContactName,
            PrimaryEmail = "kontakt@example.cz",
            PrimaryPhone = "777111222",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200
        };

    private static ApplicationUser CreateUser(string id, string email, int? personId = null) =>
        new()
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
