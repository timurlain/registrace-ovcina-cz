using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Roles;

namespace RegistraceOvcina.Web.Tests.Features.Roles;

/// <summary>
/// Covers the two /organizace/role bugs fixed in v0.9.20:
///   1. "Group by role" must pick up self-declared AdultRoleFlags (not just assigned GameRoles).
///   2. HasAccount + assigned-roles lookup must also work when the account is linked via
///      ApplicationUser.PersonId (Person.Email null or mismatched).
/// </summary>
public sealed class GameRolesViewServiceTests
{
    private const string ActorId = "actor-1";
    private const int GameId = 1;

    // ---------------------------------------------------------------
    // BuildAdultViewsAsync — HasAccount detection
    // ---------------------------------------------------------------

    [Fact]
    public async Task HasAccount_true_when_email_matches_user()
    {
        var options = CreateOptions();
        await using (var db = new ApplicationDbContext(options))
        {
            await SeedGameAsync(db);
            var user = CreateUser("user-1", "Alice", "alice@example.cz");
            db.Users.Add(user);
            await db.SaveChangesAsync();

            var person = CreatePerson("Alice", "Nováková", email: "alice@example.cz");
            await AddAdultRegistrationAsync(db, person, AdultRoleFlags.None);
        }

        var views = await CreateService(options).BuildAdultViewsAsync(GameId);

        var view = Assert.Single(views);
        Assert.True(view.HasAccount);
        Assert.Equal("user-1", view.UserId);
    }

    [Fact]
    public async Task HasAccount_true_when_PersonEmail_null_but_User_linked_via_PersonId()
    {
        var options = CreateOptions();
        int personId;
        await using (var db = new ApplicationDbContext(options))
        {
            await SeedGameAsync(db);

            // Person has NO email.
            var person = CreatePerson("Bob", "Bez-Mailu", email: null);
            db.People.Add(person);
            await db.SaveChangesAsync();
            personId = person.Id;

            // ApplicationUser is linked to the person via PersonId (AccountLinkingService path).
            var user = CreateUser("user-1", "Bob", "bob-login@example.cz");
            user.PersonId = personId;
            db.Users.Add(user);
            await db.SaveChangesAsync();

            await AddAdultRegistrationAsync(db, person, AdultRoleFlags.None);
        }

        var views = await CreateService(options).BuildAdultViewsAsync(GameId);

        var view = Assert.Single(views);
        Assert.True(view.HasAccount);
        Assert.Equal("user-1", view.UserId);
        Assert.Null(view.Email);
    }

    [Fact]
    public async Task HasAccount_false_when_no_email_and_no_PersonId_link()
    {
        var options = CreateOptions();
        await using (var db = new ApplicationDbContext(options))
        {
            await SeedGameAsync(db);
            var person = CreatePerson("Cecilie", "Neaccount", email: null);
            await AddAdultRegistrationAsync(db, person, AdultRoleFlags.None);
        }

        var views = await CreateService(options).BuildAdultViewsAsync(GameId);

        var view = Assert.Single(views);
        Assert.False(view.HasAccount);
        Assert.Null(view.UserId);
    }

    // ---------------------------------------------------------------
    // BuildAdultViewsAsync — data-quality guard
    // ---------------------------------------------------------------

    /// <summary>
    /// Regression test for v0.9.22 hotfix: /organizace/role crashed with
    /// ArgumentException ("An item with the same key has already been added. Key: 114")
    /// when two ApplicationUsers shared the same PersonId. The page must not crash;
    /// it should pick one user deterministically (earliest Id) and log a warning.
    /// </summary>
    [Fact]
    public async Task BuildAdultViewsAsync_does_not_crash_when_duplicate_personid_links_exist()
    {
        var options = CreateOptions();
        int personId;
        await using (var db = new ApplicationDbContext(options))
        {
            await SeedGameAsync(db);

            var person = CreatePerson("Bob", "Duplicate", email: null);
            db.People.Add(person);
            await db.SaveChangesAsync();
            personId = person.Id;

            // Two ApplicationUsers linked to the SAME PersonId — the exact prod data-quality
            // defect that crashed v0.9.21's .ToDictionary(l => l.PersonId, ...) call.
            var userA = CreateUser("user-a", "Bob A", "bob-a@example.cz");
            userA.PersonId = personId;
            var userB = CreateUser("user-b", "Bob B", "bob-b@example.cz");
            userB.PersonId = personId;
            db.Users.AddRange(userA, userB);
            await db.SaveChangesAsync();

            await AddAdultRegistrationAsync(db, person, AdultRoleFlags.None);
        }

        // Must not throw.
        var views = await CreateService(options).BuildAdultViewsAsync(GameId);

        var view = Assert.Single(views);
        Assert.True(view.HasAccount);
        // Deterministic pick: earliest user-id by ordinal comparison.
        Assert.Equal("user-a", view.UserId);
    }

    // ---------------------------------------------------------------
    // BuildAdultViewsAsync — assigned-roles lookup via PersonId link
    // ---------------------------------------------------------------

    [Fact]
    public async Task AssignedRoles_visible_when_linked_via_PersonId_only()
    {
        var options = CreateOptions();
        await using (var db = new ApplicationDbContext(options))
        {
            await SeedGameAsync(db);

            var person = CreatePerson("Dana", "PersonIdOnly", email: null);
            db.People.Add(person);
            await db.SaveChangesAsync();

            var user = CreateUser("user-1", "Dana", "dana-login@example.cz");
            user.PersonId = person.Id;
            db.Users.Add(user);
            await db.SaveChangesAsync();

            await AddAdultRegistrationAsync(db, person, AdultRoleFlags.None);

            // Seed an assigned game-role keyed by user.Id.
            db.GameRoles.Add(new GameRole
            {
                UserId = "user-1",
                GameId = GameId,
                RoleName = "npc",
                AssignedAtUtc = FixedDate()
            });
            await db.SaveChangesAsync();
        }

        var views = await CreateService(options).BuildAdultViewsAsync(GameId);

        var view = Assert.Single(views);
        Assert.True(view.HasAccount);
        Assert.Contains("npc", view.AssignedGameRoles);
    }

    // ---------------------------------------------------------------
    // GroupAdultsByRole
    // ---------------------------------------------------------------

    [Fact]
    public void GroupByRole_includes_self_declared_adults_even_without_assignment()
    {
        var adults = new List<AdultRoleView>
        {
            new() { LastName = "Appleby", FirstName = "A", AdultRoles = AdultRoleFlags.PlayMonster },
            new() { LastName = "Bruk",    FirstName = "B", AdultRoles = AdultRoleFlags.PlayMonster },
            new() { LastName = "Civek",   FirstName = "C", AdultRoles = AdultRoleFlags.PlayMonster }
        };

        var groups = GameRolesViewService.GroupAdultsByRole(adults);

        var monsterGroup = groups.Single(g => g.RoleName == "Příšera");
        Assert.Equal(3, monsterGroup.Adults.Count);
    }

    [Fact]
    public void GroupByRole_includes_assigned_without_self_declaration()
    {
        var adults = new List<AdultRoleView>
        {
            new()
            {
                LastName = "Dvořák", FirstName = "D",
                AdultRoles = AdultRoleFlags.None,
                AssignedGameRoles = ["npc"]
            }
        };

        var groups = GameRolesViewService.GroupAdultsByRole(adults);

        var monsterGroup = groups.Single(g => g.RoleName == "Příšera");
        Assert.Single(monsterGroup.Adults);
        Assert.DoesNotContain(groups, g => g.RoleName == "Nezvoleno");
    }

    [Fact]
    public void GroupByRole_adult_with_multiple_flags_appears_in_each_group()
    {
        var adults = new List<AdultRoleView>
        {
            new()
            {
                LastName = "Multi", FirstName = "M",
                AdultRoles = AdultRoleFlags.PlayMonster | AdultRoleFlags.TechSupport | AdultRoleFlags.RangerLeader
            }
        };

        var groups = GameRolesViewService.GroupAdultsByRole(adults);

        Assert.Contains(groups, g => g.RoleName == "Příšera" && g.Adults.Count == 1);
        Assert.Contains(groups, g => g.RoleName == "Technická pomoc" && g.Adults.Count == 1);
        Assert.Contains(groups, g => g.RoleName == "Hraničář" && g.Adults.Count == 1);
    }

    [Fact]
    public void GroupByRole_uncategorized_adults_land_in_Nezvoleno_bucket()
    {
        var adults = new List<AdultRoleView>
        {
            new() { LastName = "Blank", FirstName = "B", AdultRoles = AdultRoleFlags.None },
            new() { LastName = "Monster", FirstName = "M", AdultRoles = AdultRoleFlags.PlayMonster }
        };

        var groups = GameRolesViewService.GroupAdultsByRole(adults);

        var uncategorized = groups.Single(g => g.RoleName == "Nezvoleno");
        Assert.Single(uncategorized.Adults);
        Assert.Equal("Blank", uncategorized.Adults[0].LastName);
    }

    [Fact]
    public void GroupByRole_sorts_adults_by_last_then_first_name()
    {
        var adults = new List<AdultRoleView>
        {
            new() { LastName = "Cihlář", FirstName = "X", AdultRoles = AdultRoleFlags.PlayMonster },
            new() { LastName = "Alfa",   FirstName = "Z", AdultRoles = AdultRoleFlags.PlayMonster },
            new() { LastName = "Alfa",   FirstName = "A", AdultRoles = AdultRoleFlags.PlayMonster }
        };

        var groups = GameRolesViewService.GroupAdultsByRole(adults);
        var monster = groups.Single(g => g.RoleName == "Příšera").Adults;

        Assert.Equal("Alfa",   monster[0].LastName);
        Assert.Equal("A",      monster[0].FirstName);
        Assert.Equal("Alfa",   monster[1].LastName);
        Assert.Equal("Z",      monster[1].FirstName);
        Assert.Equal("Cihlář", monster[2].LastName);
    }

    // ---------------------------------------------------------------
    // GroupAdultsByRole — GroupingMode (v0.9.25)
    // ---------------------------------------------------------------

    // Seed shared by the three mode-comparison tests:
    //   A — self-declared PlayMonster only (no assignment)
    //   B — assigned "npc" only (no flag)
    //   C — both self-declared PlayMonster AND assigned "npc"
    private static List<AdultRoleView> SeedABC() =>
    [
        new()
        {
            LastName = "Alpha", FirstName = "A",
            AdultRoles = AdultRoleFlags.PlayMonster,
            AssignedGameRoles = []
        },
        new()
        {
            LastName = "Beta", FirstName = "B",
            AdultRoles = AdultRoleFlags.None,
            AssignedGameRoles = ["npc"]
        },
        new()
        {
            LastName = "Ceta", FirstName = "C",
            AdultRoles = AdultRoleFlags.PlayMonster,
            AssignedGameRoles = ["npc"]
        }
    ];

    [Fact]
    public void GroupByRole_AssignedMode_only_shows_assigned()
    {
        var adults = SeedABC();

        var groups = GameRolesViewService.GroupAdultsByRole(adults, GroupingMode.Assigned);

        var monster = groups.Single(g => g.RoleName == "Příšera").Adults;
        Assert.Equal(2, monster.Count);
        Assert.Contains(monster, a => a.LastName == "Beta");
        Assert.Contains(monster, a => a.LastName == "Ceta");
        Assert.DoesNotContain(monster, a => a.LastName == "Alpha");
    }

    [Fact]
    public void GroupByRole_SelfDeclaredMode_only_shows_selfdeclared()
    {
        var adults = SeedABC();

        var groups = GameRolesViewService.GroupAdultsByRole(adults, GroupingMode.SelfDeclared);

        var monster = groups.Single(g => g.RoleName == "Příšera").Adults;
        Assert.Equal(2, monster.Count);
        Assert.Contains(monster, a => a.LastName == "Alpha");
        Assert.Contains(monster, a => a.LastName == "Ceta");
        Assert.DoesNotContain(monster, a => a.LastName == "Beta");
    }

    [Fact]
    public void GroupByRole_BothMode_union()
    {
        var adults = SeedABC();

        var groups = GameRolesViewService.GroupAdultsByRole(adults, GroupingMode.Both);

        var monster = groups.Single(g => g.RoleName == "Příšera").Adults;
        Assert.Equal(3, monster.Count);
        Assert.Contains(monster, a => a.LastName == "Alpha");
        Assert.Contains(monster, a => a.LastName == "Beta");
        Assert.Contains(monster, a => a.LastName == "Ceta");
    }

    [Fact]
    public void GroupByRole_AssignedMode_catchall_is_adults_without_any_assignment()
    {
        // Self-declared PlayMonster adult with NO assignment must land in "Nezvoleno"
        // under Assigned mode — because they have no officially assigned role yet.
        // An adult with an assignment but no flag must NOT land in the catch-all.
        var adults = new List<AdultRoleView>
        {
            new()
            {
                LastName = "Flagged", FirstName = "F",
                AdultRoles = AdultRoleFlags.PlayMonster,
                AssignedGameRoles = []
            },
            new()
            {
                LastName = "Assigned", FirstName = "A",
                AdultRoles = AdultRoleFlags.None,
                AssignedGameRoles = ["npc"]
            }
        };

        var groups = GameRolesViewService.GroupAdultsByRole(adults, GroupingMode.Assigned);

        var uncategorized = groups.Single(g => g.RoleName == "Nezvoleno").Adults;
        Assert.Single(uncategorized);
        Assert.Equal("Flagged", uncategorized[0].LastName);
    }

    [Fact]
    public void GroupByRole_SelfDeclaredMode_catchall_is_adults_without_any_flag()
    {
        // Adult with assignment but NO flag must land in "Nezvoleno" under SelfDeclared mode.
        // An adult with a flag but no assignment must NOT land in the catch-all.
        var adults = new List<AdultRoleView>
        {
            new()
            {
                LastName = "Flagged", FirstName = "F",
                AdultRoles = AdultRoleFlags.PlayMonster,
                AssignedGameRoles = []
            },
            new()
            {
                LastName = "Assigned", FirstName = "A",
                AdultRoles = AdultRoleFlags.None,
                AssignedGameRoles = ["npc"]
            }
        };

        var groups = GameRolesViewService.GroupAdultsByRole(adults, GroupingMode.SelfDeclared);

        var uncategorized = groups.Single(g => g.RoleName == "Nezvoleno").Adults;
        Assert.Single(uncategorized);
        Assert.Equal("Assigned", uncategorized[0].LastName);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static GameRolesViewService CreateService(DbContextOptions<ApplicationDbContext> options)
        => new(new TestDbContextFactory(options));

    private static async Task SeedGameAsync(ApplicationDbContext db)
    {
        db.Games.Add(CreateGame(GameId));
        await db.SaveChangesAsync();
    }

    private static async Task AddAdultRegistrationAsync(
        ApplicationDbContext db,
        Person person,
        AdultRoleFlags flags)
    {
        if (person.Id == 0)
        {
            db.People.Add(person);
            await db.SaveChangesAsync();
        }

        var submission = new RegistrationSubmission
        {
            GameId = GameId,
            RegistrantUserId = "registrant-1",
            PrimaryContactName = $"{person.FirstName} {person.LastName}",
            GroupName = "Test group",
            PrimaryEmail = person.Email ?? "primary@example.cz",
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
            AdultRoles = flags,
            Status = RegistrationStatus.Active,
            CreatedAtUtc = FixedDate(),
            UpdatedAtUtc = FixedDate()
        });
        await db.SaveChangesAsync();
    }

    private static Person CreatePerson(string firstName, string lastName, string? email) => new()
    {
        FirstName = firstName,
        LastName = lastName,
        BirthYear = 1990,
        Email = email,
        CreatedAtUtc = FixedDate(),
        UpdatedAtUtc = FixedDate()
    };

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

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ApplicationDbContext(options));
    }
}
