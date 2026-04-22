using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Roles;

namespace RegistraceOvcina.Web.Tests.Features.Roles;

/// <summary>
/// Covers the /organizace/role view-service:
///   1. BuildAdultViewsAsync: HasAccount detection via email OR PersonId, with defensive guard
///      against duplicate PersonId links (v0.9.22 regression).
///   2. GroupAdultsByRole: groups by officially-assigned GameRole only (v0.9.26 simplification —
///      the 3-mode toggle Přidělené/Zvolené/Obě has been retired).
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
    // GroupAdultsByRole — v0.9.26: groups by assigned GameRole only
    // ---------------------------------------------------------------

    [Fact]
    public void GroupByRole_groups_by_assigned()
    {
        // Three adults, two assigned to "npc", one assigned to "helper".
        var adults = new List<AdultRoleView>
        {
            new() { LastName = "Alfa",  FirstName = "A", AssignedGameRoles = ["npc"] },
            new() { LastName = "Bruk",  FirstName = "B", AssignedGameRoles = ["npc"] },
            new() { LastName = "Civek", FirstName = "C", AssignedGameRoles = ["helper"] }
        };

        var groups = GameRolesViewService.GroupAdultsByRole(adults);

        var npc = groups.Single(g => g.RoleName == "npc");
        Assert.Equal(2, npc.Adults.Count);
        Assert.Equal("Alfa",  npc.Adults[0].LastName);
        Assert.Equal("Bruk",  npc.Adults[1].LastName);

        var helper = groups.Single(g => g.RoleName == "helper");
        Assert.Single(helper.Adults);
        Assert.Equal("Civek", helper.Adults[0].LastName);

        // No "Nezvoleno" bucket because every adult has at least one assigned role.
        Assert.DoesNotContain(groups, g => g.RoleName == "Nezvoleno");
    }

    [Fact]
    public void GroupByRole_catchall_shows_unassigned()
    {
        // Two adults, one assigned ("npc"), one with no assignment — even though they
        // have a self-declared AdultRoles flag, they must land in "Nezvoleno" because
        // grouping is by assignment only (v0.9.26).
        var adults = new List<AdultRoleView>
        {
            new()
            {
                LastName = "Assigned", FirstName = "A",
                AdultRoles = AdultRoleFlags.None,
                AssignedGameRoles = ["npc"]
            },
            new()
            {
                LastName = "Unassigned", FirstName = "U",
                AdultRoles = AdultRoleFlags.PlayMonster,
                AssignedGameRoles = []
            }
        };

        var groups = GameRolesViewService.GroupAdultsByRole(adults);

        var nezvoleno = groups.Single(g => g.RoleName == "Nezvoleno");
        Assert.Single(nezvoleno.Adults);
        Assert.Equal("Unassigned", nezvoleno.Adults[0].LastName);
    }

    [Fact]
    public void GroupByRole_adult_with_multiple_assigned_roles_appears_in_each_group()
    {
        var adults = new List<AdultRoleView>
        {
            new()
            {
                LastName = "Multi", FirstName = "M",
                AssignedGameRoles = ["npc", "helper", "ranger-leader"]
            }
        };

        var groups = GameRolesViewService.GroupAdultsByRole(adults);

        Assert.Contains(groups, g => g.RoleName == "npc" && g.Adults.Count == 1);
        Assert.Contains(groups, g => g.RoleName == "helper" && g.Adults.Count == 1);
        Assert.Contains(groups, g => g.RoleName == "ranger-leader" && g.Adults.Count == 1);
    }

    [Fact]
    public void GroupByRole_sorts_adults_by_last_then_first_name()
    {
        var adults = new List<AdultRoleView>
        {
            new() { LastName = "Cihlář", FirstName = "X", AssignedGameRoles = ["npc"] },
            new() { LastName = "Alfa",   FirstName = "Z", AssignedGameRoles = ["npc"] },
            new() { LastName = "Alfa",   FirstName = "A", AssignedGameRoles = ["npc"] }
        };

        var groups = GameRolesViewService.GroupAdultsByRole(adults);
        var npc = groups.Single(g => g.RoleName == "npc").Adults;

        Assert.Equal("Alfa",   npc[0].LastName);
        Assert.Equal("A",      npc[0].FirstName);
        Assert.Equal("Alfa",   npc[1].LastName);
        Assert.Equal("Z",      npc[1].FirstName);
        Assert.Equal("Cihlář", npc[2].LastName);
    }

    [Fact]
    public void GroupByRole_available_role_names_emit_empty_groups()
    {
        // When caller passes availableRoleNames, the result includes a group for every
        // known role — even if no adult is currently assigned — so organizers see that
        // the role exists and is open for assignment.
        var adults = new List<AdultRoleView>
        {
            new() { LastName = "Alfa", FirstName = "A", AssignedGameRoles = ["npc"] }
        };

        var available = new List<string> { "npc", "helper", "tech-support" };

        var groups = GameRolesViewService.GroupAdultsByRole(adults, available);

        Assert.Contains(groups, g => g.RoleName == "npc" && g.Adults.Count == 1);
        Assert.Contains(groups, g => g.RoleName == "helper" && g.Adults.Count == 0);
        Assert.Contains(groups, g => g.RoleName == "tech-support" && g.Adults.Count == 0);
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
