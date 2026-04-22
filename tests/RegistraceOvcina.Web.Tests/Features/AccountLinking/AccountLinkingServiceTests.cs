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

    // 14a -----------------------------------------------------------
    // v0.9.22 hotfix: guard against creating duplicate PersonId links.
    [Fact]
    public async Task LinkAsync_throws_when_person_already_linked_to_another_user()
    {
        var options = CreateOptions();
        int personId;

        await using (var db = new ApplicationDbContext(options))
        {
            var person = new Person
            {
                FirstName = "Shared",
                LastName = "Person",
                BirthYear = 1990,
                Email = null,
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(person);
            await db.SaveChangesAsync();
            personId = person.Id;

            // user-existing already claims the person.
            var existing = CreateUser("user-existing", "Existing", "existing@example.cz");
            existing.PersonId = personId;
            db.Users.Add(existing);
            // user-new is the one we'll try to link — and it should fail.
            db.Users.Add(CreateUser("user-new", "New", "new@example.cz"));
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LinkAsync("user-new", personId, ActorId, CancellationToken.None));

        // Existing link must be unchanged; new user must NOT be linked.
        await using var verify = new ApplicationDbContext(options);
        var existingUser = await verify.Users.SingleAsync(u => u.Id == "user-existing");
        var newUser = await verify.Users.SingleAsync(u => u.Id == "user-new");
        Assert.Equal(personId, existingUser.PersonId);
        Assert.Null(newUser.PersonId);
    }

    // 14b -----------------------------------------------------------
    [Fact]
    public async Task AutoLinkHighConfidenceAsync_skips_proposals_whose_person_already_linked()
    {
        // In-memory provider doesn't enforce Person.Email uniqueness, so we can have two
        // ApplicationUsers matched to the same Person's email and observe that AutoLink
        // only links the first and skips the second (instead of creating a duplicate).
        var options = CreateOptions();

        await using (var db = new ApplicationDbContext(options))
        {
            // user-a is already linked to the target Person.
            var person = new Person
            {
                FirstName = "Alice",
                LastName = "Shared",
                BirthYear = 1990,
                Email = "shared@example.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(person);
            await db.SaveChangesAsync();

            // user-b: unlinked, but shares the same normalized email as the Person.
            // Without the guard, ProposeAsync would propose user-b → person, and
            // AutoLink would create the duplicate that crashes /organizace/role.
            var userA = CreateUser("user-a", "Alice Primary", "shared@example.cz");
            userA.PersonId = person.Id;
            db.Users.Add(userA);
            db.Users.Add(CreateUser("user-b", "Alice Other", "shared@example.cz"));
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);
        var count = await service.AutoLinkHighConfidenceAsync(ActorId, CancellationToken.None);

        // user-b must NOT be linked because user-a already owns the Person.
        // ProposeAsync already filters already-linked Persons out, so count is 0.
        Assert.Equal(0, count);

        await using var verify = new ApplicationDbContext(options);
        var userB = await verify.Users.SingleAsync(u => u.Id == "user-b");
        Assert.Null(userB.PersonId);

        // And only one user points at the Person.
        var linkedCount = await verify.Users.CountAsync(u => u.PersonId != null);
        Assert.Equal(1, linkedCount);
    }

    // 14c -----------------------------------------------------------
    [Fact]
    public async Task ProposeAsync_excludes_persons_already_linked_to_another_user()
    {
        var options = CreateOptions();

        await using (var db = new ApplicationDbContext(options))
        {
            var person = new Person
            {
                FirstName = "Taken",
                LastName = "Person",
                BirthYear = 1990,
                Email = "taken@example.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(person);
            await db.SaveChangesAsync();

            // user-owner already owns the Person.
            var owner = CreateUser("user-owner", "Owner", "taken@example.cz");
            owner.PersonId = person.Id;
            db.Users.Add(owner);

            // user-candidate shares the email but the Person is already claimed —
            // ProposeAsync must NOT surface it as a proposal.
            db.Users.Add(CreateUser("user-candidate", "Candidate", "taken@example.cz"));
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);
        var bucket = await service.ProposeAsync(CancellationToken.None);

        Assert.Empty(bucket.HighConfidence);
        Assert.Empty(bucket.MediumConfidence);
    }

    // 16a -----------------------------------------------------------
    // v0.9.23: ListConflictsAsync exposes Persons with >1 linked ApplicationUser
    // so the admin can clean them up via the new Konflikty tab.
    [Fact]
    public async Task ListConflictsAsync_empty_when_no_duplicates()
    {
        var options = CreateOptions();

        await using (var db = new ApplicationDbContext(options))
        {
            var person = new Person
            {
                FirstName = "Alice",
                LastName = "Solo",
                BirthYear = 1990,
                Email = "alice@example.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(person);
            await db.SaveChangesAsync();

            var user = CreateUser("user-1", "Alice", "alice@example.cz");
            user.PersonId = person.Id;
            db.Users.Add(user);
            db.Users.Add(CreateUser("user-unlinked", "Nobody", "nobody@example.cz"));
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);
        var conflicts = await service.ListConflictsAsync(CancellationToken.None);

        Assert.Empty(conflicts);
    }

    // 16b -----------------------------------------------------------
    [Fact]
    public async Task ListConflictsAsync_returns_each_person_with_multiple_links()
    {
        var options = CreateOptions();
        int personIdA, personIdB;

        await using (var db = new ApplicationDbContext(options))
        {
            var personA = new Person
            {
                FirstName = "Alice",
                LastName = "Conflicted",
                BirthYear = 1990,
                Email = "alice@example.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            var personB = new Person
            {
                FirstName = "Bob",
                LastName = "AlsoConflicted",
                BirthYear = 1985,
                Email = null,
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.AddRange(personA, personB);
            await db.SaveChangesAsync();
            personIdA = personA.Id;
            personIdB = personB.Id;

            // Two users on personA.
            var a1 = CreateUser("user-a1", "Alice One", "a1@example.cz");
            a1.PersonId = personIdA;
            var a2 = CreateUser("user-a2", "Alice Two", "a2@example.cz");
            a2.PersonId = personIdA;

            // Three users on personB.
            var b1 = CreateUser("user-b1", "Bob One", "b1@example.cz");
            b1.PersonId = personIdB;
            var b2 = CreateUser("user-b2", "Bob Two", "b2@example.cz");
            b2.PersonId = personIdB;
            var b3 = CreateUser("user-b3", "Bob Three", "b3@example.cz");
            b3.PersonId = personIdB;

            db.Users.AddRange(a1, a2, b1, b2, b3);
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);
        var conflicts = await service.ListConflictsAsync(CancellationToken.None);

        Assert.Equal(2, conflicts.Count);

        var aliceConflict = Assert.Single(conflicts, c => c.PersonId == personIdA);
        Assert.Equal("Alice Conflicted", aliceConflict.PersonFullName);
        Assert.Equal(1990, aliceConflict.PersonBirthYear);
        Assert.Equal("alice@example.cz", aliceConflict.PersonEmail);
        Assert.Equal(2, aliceConflict.LinkedUsers.Count);

        var bobConflict = Assert.Single(conflicts, c => c.PersonId == personIdB);
        Assert.Equal(3, bobConflict.LinkedUsers.Count);
        Assert.Null(bobConflict.PersonEmail);
    }

    // 16c -----------------------------------------------------------
    [Fact]
    public async Task ListConflictsAsync_includes_linkedat_from_auditlog()
    {
        var options = CreateOptions();
        int personId;
        var olderAudit = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var newerAudit = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);

        await using (var db = new ApplicationDbContext(options))
        {
            var person = new Person
            {
                FirstName = "Alice",
                LastName = "WithAudit",
                BirthYear = 1990,
                Email = null,
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(person);
            await db.SaveChangesAsync();
            personId = person.Id;

            var u1 = CreateUser("user-older", "Older", "older@example.cz");
            u1.PersonId = personId;
            var u2 = CreateUser("user-newer", "Newer", "newer@example.cz");
            u2.PersonId = personId;
            db.Users.AddRange(u1, u2);

            db.AuditLogs.AddRange(
                new AuditLog
                {
                    EntityType = "ApplicationUser",
                    EntityId = "user-older",
                    Action = "LinkAccount",
                    ActorUserId = ActorId,
                    CreatedAtUtc = olderAudit,
                    DetailsJson = "{}"
                },
                new AuditLog
                {
                    EntityType = "ApplicationUser",
                    EntityId = "user-newer",
                    Action = "LinkAccount",
                    ActorUserId = ActorId,
                    CreatedAtUtc = newerAudit,
                    DetailsJson = "{}"
                });
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);
        var conflicts = await service.ListConflictsAsync(CancellationToken.None);

        var conflict = Assert.Single(conflicts);
        var olderUser = Assert.Single(conflict.LinkedUsers, u => u.UserId == "user-older");
        var newerUser = Assert.Single(conflict.LinkedUsers, u => u.UserId == "user-newer");
        Assert.NotNull(olderUser.LinkedAtUtc);
        Assert.NotNull(newerUser.LinkedAtUtc);
        Assert.Equal(olderAudit, olderUser.LinkedAtUtc!.Value.UtcDateTime);
        Assert.Equal(newerAudit, newerUser.LinkedAtUtc!.Value.UtcDateTime);
    }

    // 16d -----------------------------------------------------------
    [Fact]
    public async Task ListConflictsAsync_users_ordered_most_recent_first()
    {
        var options = CreateOptions();
        int personId;
        var oldest = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var middle = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var newest = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);

        await using (var db = new ApplicationDbContext(options))
        {
            var person = new Person
            {
                FirstName = "Shared",
                LastName = "Ordered",
                BirthYear = 1990,
                Email = null,
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(person);
            await db.SaveChangesAsync();
            personId = person.Id;

            var u1 = CreateUser("user-oldest", "Old", "old@example.cz");
            u1.PersonId = personId;
            var u2 = CreateUser("user-middle", "Mid", "mid@example.cz");
            u2.PersonId = personId;
            var u3 = CreateUser("user-newest", "New", "new@example.cz");
            u3.PersonId = personId;
            db.Users.AddRange(u1, u2, u3);

            db.AuditLogs.AddRange(
                new AuditLog { EntityType = "ApplicationUser", EntityId = "user-oldest", Action = "LinkAccount", ActorUserId = ActorId, CreatedAtUtc = oldest, DetailsJson = "{}" },
                new AuditLog { EntityType = "ApplicationUser", EntityId = "user-middle", Action = "LinkAccount", ActorUserId = ActorId, CreatedAtUtc = middle, DetailsJson = "{}" },
                new AuditLog { EntityType = "ApplicationUser", EntityId = "user-newest", Action = "LinkAccount", ActorUserId = ActorId, CreatedAtUtc = newest, DetailsJson = "{}" });
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);
        var conflicts = await service.ListConflictsAsync(CancellationToken.None);

        var conflict = Assert.Single(conflicts);
        var orderedIds = conflict.LinkedUsers.Select(u => u.UserId).ToList();
        Assert.Equal(new[] { "user-newest", "user-middle", "user-oldest" }, orderedIds);
    }

    // 16e -----------------------------------------------------------
    // Regression guard: a Person with exactly one linked user is the normal state,
    // and must NEVER appear on the conflicts list.
    [Fact]
    public async Task ListConflictsAsync_single_linked_user_not_listed()
    {
        var options = CreateOptions();

        await using (var db = new ApplicationDbContext(options))
        {
            var person = new Person
            {
                FirstName = "Alice",
                LastName = "Normal",
                BirthYear = 1990,
                Email = "alice@example.cz",
                CreatedAtUtc = FixedDate(),
                UpdatedAtUtc = FixedDate()
            };
            db.People.Add(person);
            await db.SaveChangesAsync();

            var user = CreateUser("user-1", "Alice", "alice@example.cz");
            user.PersonId = person.Id;
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var service = CreateService(options);
        var conflicts = await service.ListConflictsAsync(CancellationToken.None);

        Assert.Empty(conflicts);
    }

    // 15 -----------------------------------------------------------
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
