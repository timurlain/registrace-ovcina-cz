using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.AccountLinking;

namespace RegistraceOvcina.Web.Tests.Features.AccountLinking;

public sealed class AccountLinkingServiceTests
{
    private const string ActorId = "actor-1";

    // 1 ------------------------------------------------------------
    [Fact]
    public async Task ProposeAsync_empty_db_returns_empty_buckets()
    {
        var options = CreateOptions();
        await EnsureCreatedAsync(options);

        var service = CreateService(options);

        var bucket = await service.ProposeAsync(CancellationToken.None);

        Assert.Empty(bucket.HighConfidence);
        Assert.Empty(bucket.MediumConfidence);
    }

    // 2 ------------------------------------------------------------
    [Fact]
    public async Task ProposeAsync_exact_email_match_goes_to_HighConfidence()
    {
        var options = CreateOptions();

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(CreateUser("user-1", "Alice", "alice@example.cz"));
            db.People.Add(new Person
            {
                FirstName = "Alice",
                LastName = "Novotná",
                BirthYear = 1990,
                Email = "alice@example.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);

        var bucket = await service.ProposeAsync(CancellationToken.None);

        var proposal = Assert.Single(bucket.HighConfidence);
        Assert.Equal("user-1", proposal.UserId);
        Assert.Equal(LinkSignal.ExactEmailMatch, proposal.Signal);
        Assert.Empty(bucket.MediumConfidence);
    }

    // 3 ------------------------------------------------------------
    [Fact]
    public async Task ProposeAsync_alternate_email_match_goes_to_HighConfidence()
    {
        var options = CreateOptions();

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(CreateUser("user-1", "Alice", "alice@example.cz"));
            db.UserEmails.Add(new UserEmail
            {
                UserId = "user-1",
                Email = "alice-alt@example.cz",
                NormalizedEmail = "ALICE-ALT@EXAMPLE.CZ",
                CreatedAtUtc = FixedDate()
            });
            db.People.Add(new Person
            {
                FirstName = "Alice",
                LastName = "Svobodová",
                BirthYear = 1990,
                Email = "alice-alt@example.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);

        var bucket = await service.ProposeAsync(CancellationToken.None);

        var proposal = Assert.Single(bucket.HighConfidence);
        Assert.Equal(LinkSignal.AlternateEmailMatch, proposal.Signal);
    }

    // 4 ------------------------------------------------------------
    [Fact]
    public async Task ProposeAsync_submission_primary_with_name_match_goes_to_HighConfidence()
    {
        var options = CreateOptions();
        int personId;

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(CreateUser("user-1", "Tomáš Pajonk", "tomas@example.cz"));
            db.Games.Add(CreateGame(1));
            var person = new Person
            {
                FirstName = "Tomáš",
                LastName = "Pajonk",
                BirthYear = 1980,
                // No email on the person — this is exactly the case SubmissionPrimaryContactMatch exists to cover.
                Email = null,
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(person);
            await db.SaveChangesAsync();
            personId = person.Id;

            var submission = new RegistrationSubmission
            {
                GameId = 1,
                RegistrantUserId = "user-1",
                PrimaryContactName = "Tomáš Pajonk",
                GroupName = "Rodina Pajonk",
                PrimaryEmail = "tomas@example.cz",
                PrimaryPhone = "+420 777 111 222",
                Status = SubmissionStatus.Submitted,
                LastEditedAtUtc = FixedDate()
            };
            db.RegistrationSubmissions.Add(submission);
            await db.SaveChangesAsync();

            db.Registrations.Add(new Registration
            {
                SubmissionId = submission.Id,
                PersonId = personId,
                AttendeeType = AttendeeType.Adult,
                Status = RegistrationStatus.Active,
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);
        var bucket = await service.ProposeAsync(CancellationToken.None);

        var proposal = Assert.Single(bucket.HighConfidence);
        Assert.Equal(LinkSignal.SubmissionPrimaryContactMatch, proposal.Signal);
        Assert.Equal(personId, proposal.PersonId);
    }

    // 5 ------------------------------------------------------------
    [Fact]
    public async Task ProposeAsync_submission_primary_name_mismatch_not_proposed()
    {
        var options = CreateOptions();

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(CreateUser("user-1", "Unrelated Display", "tomas@example.cz"));
            db.Games.Add(CreateGame(1));
            var person = new Person
            {
                // Different name from the Submission.PrimaryContactName.
                FirstName = "Jana",
                LastName = "Nováková",
                BirthYear = 1995,
                Email = null,
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(person);
            await db.SaveChangesAsync();

            var submission = new RegistrationSubmission
            {
                GameId = 1,
                RegistrantUserId = "user-1",
                PrimaryContactName = "Tomáš Pajonk",
                GroupName = "Family",
                PrimaryEmail = "tomas@example.cz",
                PrimaryPhone = "555",
                Status = SubmissionStatus.Submitted,
                LastEditedAtUtc = FixedDate()
            };
            db.RegistrationSubmissions.Add(submission);
            await db.SaveChangesAsync();

            db.Registrations.Add(new Registration
            {
                SubmissionId = submission.Id,
                PersonId = person.Id,
                AttendeeType = AttendeeType.Adult,
                Status = RegistrationStatus.Active,
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);
        var bucket = await service.ProposeAsync(CancellationToken.None);

        Assert.Empty(bucket.HighConfidence);
    }

    // 6 ------------------------------------------------------------
    [Fact]
    public async Task ProposeAsync_ambiguous_multiple_persons_same_email_skipped()
    {
        var options = CreateOptions();

        // In-memory provider doesn't honor the unique-email filter, so we can exercise
        // the "tie" branch even though prod Postgres would disallow duplicates.
        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(CreateUser("user-1", "Alice", "shared@example.cz"));
            db.People.AddRange(
                new Person
                {
                    FirstName = "Alice",
                    LastName = "One",
                    BirthYear = 1990,
                    Email = "shared@example.cz",
                    CreatedAtUtc = FixedDate(),
                    UpdatedAtUtc = FixedDate()
                },
                new Person
                {
                    FirstName = "Alice",
                    LastName = "Two",
                    BirthYear = 1991,
                    Email = "shared@example.cz",
                    CreatedAtUtc = FixedDate(),
                    UpdatedAtUtc = FixedDate()
                });
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);
        var bucket = await service.ProposeAsync(CancellationToken.None);

        Assert.Empty(bucket.HighConfidence);
        Assert.Empty(bucket.MediumConfidence);
    }

    // 7 ------------------------------------------------------------
    [Fact]
    public async Task ProposeAsync_already_linked_user_excluded()
    {
        var options = CreateOptions();

        await using (var db = new ApplicationDbContext(options))
        {
            var person = new Person
            {
                FirstName = "Alice",
                LastName = "Exists",
                BirthYear = 1990,
                Email = "alice@example.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(person);
            await db.SaveChangesAsync();

            var linkedUser = CreateUser("user-1", "Alice", "alice@example.cz");
            linkedUser.PersonId = person.Id;
            db.Users.Add(linkedUser);
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);
        var bucket = await service.ProposeAsync(CancellationToken.None);

        Assert.Empty(bucket.HighConfidence);
        Assert.Empty(bucket.MediumConfidence);
    }

    // 8 ------------------------------------------------------------
    [Fact]
    public async Task ProposeAsync_fuzzy_name_match_goes_to_MediumConfidence()
    {
        var options = CreateOptions();

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(CreateUser("user-1", "Tomas Pajonk", "someone@example.cz"));
            db.People.Add(new Person
            {
                // Same last name, first name differs by one character (diacritic + accent).
                FirstName = "Tomáš",
                LastName = "Pajonk",
                BirthYear = 1980,
                Email = null,
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);
        var bucket = await service.ProposeAsync(CancellationToken.None);

        Assert.Empty(bucket.HighConfidence);
        var proposal = Assert.Single(bucket.MediumConfidence);
        Assert.Equal(LinkSignal.FuzzyNameMatch, proposal.Signal);
        Assert.NotNull(proposal.FuzzyScore);
        Assert.True(proposal.FuzzyScore >= 75);
    }

    // 9 ------------------------------------------------------------
    [Fact]
    public async Task AutoLinkHighConfidenceAsync_sets_PersonId_and_writes_AuditLog()
    {
        var options = CreateOptions();

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(CreateUser("user-1", "Alice", "alice@example.cz"));
            db.People.Add(new Person
            {
                FirstName = "Alice",
                LastName = "Example",
                BirthYear = 1990,
                Email = "alice@example.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);
        var count = await service.AutoLinkHighConfidenceAsync(ActorId, CancellationToken.None);

        Assert.Equal(1, count);

        await using var verify = new ApplicationDbContext(options);
        var user = await verify.Users.SingleAsync(u => u.Id == "user-1");
        Assert.NotNull(user.PersonId);

        var audit = await verify.AuditLogs.SingleAsync();
        Assert.Equal("ApplicationUser", audit.EntityType);
        Assert.Equal("user-1", audit.EntityId);
        Assert.Equal("LinkAccount", audit.Action);
        Assert.Equal(ActorId, audit.ActorUserId);
        Assert.NotNull(audit.DetailsJson);
        Assert.Contains("ExactEmailMatch", audit.DetailsJson);
    }

    // 10 -----------------------------------------------------------
    [Fact]
    public async Task AutoLinkHighConfidenceAsync_idempotent_second_run_noop()
    {
        var options = CreateOptions();

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(CreateUser("user-1", "Alice", "alice@example.cz"));
            db.People.Add(new Person
            {
                FirstName = "Alice",
                LastName = "Example",
                BirthYear = 1990,
                Email = "alice@example.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            });
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);
        var first = await service.AutoLinkHighConfidenceAsync(ActorId, CancellationToken.None);
        var second = await service.AutoLinkHighConfidenceAsync(ActorId, CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(0, second);

        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(1, await verify.AuditLogs.CountAsync());
    }

    // 11 -----------------------------------------------------------
    [Fact]
    public async Task LinkAsync_manual_link_writes_AuditLog()
    {
        var options = CreateOptions();
        int personId;

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(CreateUser("user-1", "Alice", "alice@example.cz"));
            var person = new Person
            {
                FirstName = "Alice",
                LastName = "Manual",
                BirthYear = 1990,
                Email = null,
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(person);
            await db.SaveChangesAsync();
            personId = person.Id;
        }

        var service = CreateService(options);
        await service.LinkAsync("user-1", personId, ActorId, CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var user = await verify.Users.SingleAsync(u => u.Id == "user-1");
        Assert.Equal(personId, user.PersonId);
        var audit = await verify.AuditLogs.SingleAsync();
        Assert.Equal("LinkAccount", audit.Action);
        Assert.Contains("Manual", audit.DetailsJson!);
    }

    // 12 -----------------------------------------------------------
    [Fact]
    public async Task UnlinkAsync_clears_PersonId_writes_AuditLog()
    {
        var options = CreateOptions();
        int personId;

        await using (var db = new ApplicationDbContext(options))
        {
            var person = new Person
            {
                FirstName = "Alice",
                LastName = "Linked",
                BirthYear = 1990,
                Email = "a@b.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(person);
            await db.SaveChangesAsync();
            personId = person.Id;

            var seededUser = CreateUser("user-1", "Alice", "alice@example.cz");
            seededUser.PersonId = personId;
            db.Users.Add(seededUser);
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);
        await service.UnlinkAsync("user-1", ActorId, CancellationToken.None);

        await using var verify = new ApplicationDbContext(options);
        var user = await verify.Users.SingleAsync(u => u.Id == "user-1");
        Assert.Null(user.PersonId);
        var audit = await verify.AuditLogs.SingleAsync();
        Assert.Equal("UnlinkAccount", audit.Action);
    }

    // 13 -----------------------------------------------------------
    [Fact]
    public async Task ListUnlinkedPersonsAsync_excludes_persons_with_linked_user()
    {
        var options = CreateOptions();

        await using (var db = new ApplicationDbContext(options))
        {
            var linkedPerson = new Person
            {
                FirstName = "Linked",
                LastName = "One",
                BirthYear = 1990,
                Email = "l@x.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            var freePerson = new Person
            {
                FirstName = "Free",
                LastName = "Two",
                BirthYear = 1995,
                Email = "f@x.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.AddRange(linkedPerson, freePerson);
            await db.SaveChangesAsync();

            var user = CreateUser("user-1", "Linked User", "u@x.cz");
            user.PersonId = linkedPerson.Id;
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);
        var list = await service.ListUnlinkedPersonsAsync(CancellationToken.None);

        var row = Assert.Single(list);
        Assert.Equal("Free Two", row.PersonFullName);
    }

    // 14 -----------------------------------------------------------
    [Fact]
    public async Task SearchUsersAsync_matches_email_and_displayname()
    {
        var options = CreateOptions();

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.AddRange(
                CreateUser("user-1", "Alice Novotná", "alice@example.cz"),
                CreateUser("user-2", "Bob Dvořák", "bob@example.cz"),
                CreateUser("user-3", "Someone Else", "noreply@example.cz"));
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);

        var byEmail = await service.SearchUsersAsync("alice", 10, CancellationToken.None);
        Assert.Contains(byEmail, r => r.UserId == "user-1");

        var byDisplayName = await service.SearchUsersAsync("DVOŘ", 10, CancellationToken.None);
        Assert.Contains(byDisplayName, r => r.UserId == "user-2");

        var tooShort = await service.SearchUsersAsync("a", 10, CancellationToken.None);
        Assert.Empty(tooShort);
    }

    // -------- helpers --------

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static async Task EnsureCreatedAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
    }

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

    private static DateTime FixedDate() => new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    private static AccountLinkingService CreateService(DbContextOptions<ApplicationDbContext> options)
        => new(new TestDbContextFactory(options), new FixedTimeProvider(), NullLogger<AccountLinkingService>.Instance);

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
}
