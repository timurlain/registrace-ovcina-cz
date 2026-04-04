using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RegistraceOvcina.Web.Components.Pages.Admin;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Users;
using RegistraceOvcina.Web.Security;

namespace RegistraceOvcina.Web.Tests;

public sealed class UserAdministrationServiceTests
{
    [Fact]
    public void UserManagementPage_RequiresAdminPolicy()
    {
        var authorizeAttribute = typeof(UserManagement)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .Single();

        Assert.Equal(AuthorizationPolicies.AdminOnly, authorizeAttribute.Policy);
    }

    [Fact]
    public async Task GetPageAsync_GroupsStaffUsersAndMarksLastActiveAdmin()
    {
        var options = CreateOptions();
        var admin = CreateUser("admin-id", "Admin", "admin@example.cz", true);
        var organizer = CreateUser("organizer-id", "Organizer", "organizer@example.cz", true);
        var registrant = CreateUser("registrant-id", "Registrant", "registrant@example.cz", true);

        await using (var db = new ApplicationDbContext(options))
        {
            SeedRole(db, "role-admin", RoleNames.Admin);
            SeedRole(db, "role-organizer", RoleNames.Organizer);

            db.Users.AddRange(admin, organizer, registrant);
            db.UserRoles.AddRange(
                new IdentityUserRole<string> { UserId = admin.Id, RoleId = "role-admin" },
                new IdentityUserRole<string> { UserId = organizer.Id, RoleId = "role-organizer" });

            await db.SaveChangesAsync();
        }

        var service = new UserAdministrationService(new TestDbContextFactory(options), new FixedTimeProvider());

        var page = await service.GetPageAsync();

        Assert.Equal(3, page.TotalUsers);
        Assert.Equal(2, page.ActiveStaffCount);
        Assert.Equal(1, page.AdminCount);
        Assert.Equal(2, page.StaffUsers.Count);
        Assert.Single(page.OtherUsers);
        Assert.Contains(page.StaffUsers, x => x.Id == admin.Id && x.IsAdmin && x.IsLastActiveAdmin);
        Assert.Contains(page.StaffUsers, x => x.Id == organizer.Id && x.IsOrganizer && !x.IsAdmin);
        Assert.Equal(registrant.Id, page.OtherUsers[0].Id);
    }

    [Fact]
    public async Task ToggleRoleAsync_AddsAndRemovesRolesAndWritesAuditLog()
    {
        var options = CreateOptions();
        var actor = CreateUser("actor-id", "Admin", "admin@example.cz", true);
        var target = CreateUser("target-id", "Target", "target@example.cz", true);

        await using (var db = new ApplicationDbContext(options))
        {
            SeedRole(db, "role-admin", RoleNames.Admin);
            SeedRole(db, "role-organizer", RoleNames.Organizer);

            db.Users.AddRange(actor, target);
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = actor.Id, RoleId = "role-admin" });
            await db.SaveChangesAsync();
        }

        var service = new UserAdministrationService(new TestDbContextFactory(options), new FixedTimeProvider());

        var added = await service.ToggleRoleAsync(target.Id, RoleNames.Organizer, actor.Id);
        var removed = await service.ToggleRoleAsync(target.Id, RoleNames.Organizer, actor.Id);

        Assert.Equal("organizer-added", added.StatusCode);
        Assert.Equal("organizer-removed", removed.StatusCode);

        await using var verificationDb = new ApplicationDbContext(options);
        Assert.DoesNotContain(verificationDb.UserRoles, x => x.UserId == target.Id && x.RoleId == "role-organizer");

        var audits = await verificationDb.AuditLogs
            .Where(x => x.EntityId == target.Id)
            .OrderBy(x => x.Id)
            .ToListAsync();

        Assert.Collection(
            audits,
            first => Assert.Equal("UserRoleGranted", first.Action),
            second => Assert.Equal("UserRoleRevoked", second.Action));
    }

    [Fact]
    public async Task ToggleRoleAsync_BlocksRemovingLastActiveAdmin()
    {
        var options = CreateOptions();
        var actor = CreateUser("actor-id", "Admin", "admin@example.cz", true);

        await using (var db = new ApplicationDbContext(options))
        {
            SeedRole(db, "role-admin", RoleNames.Admin);

            db.Users.Add(actor);
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = actor.Id, RoleId = "role-admin" });
            await db.SaveChangesAsync();
        }

        var service = new UserAdministrationService(new TestDbContextFactory(options), new FixedTimeProvider());

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.ToggleRoleAsync(actor.Id, RoleNames.Admin, actor.Id));

        Assert.Equal("Posledního aktivního správce nelze odebrat ani deaktivovat.", ex.Message);
    }

    [Fact]
    public async Task ToggleActiveAsync_BlocksDeactivatingLastActiveAdminAndAuditsOtherDeactivation()
    {
        var options = CreateOptions();
        var actor = CreateUser("actor-id", "Admin", "admin@example.cz", true);
        var target = CreateUser("target-id", "Target", "target@example.cz", true);

        await using (var db = new ApplicationDbContext(options))
        {
            SeedRole(db, "role-admin", RoleNames.Admin);

            db.Users.AddRange(actor, target);
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = actor.Id, RoleId = "role-admin" });
            await db.SaveChangesAsync();
        }

        var service = new UserAdministrationService(new TestDbContextFactory(options), new FixedTimeProvider());

        var result = await service.ToggleActiveAsync(target.Id, actor.Id);

        Assert.Equal("user-deactivated", result.StatusCode);

        await using (var verificationDb = new ApplicationDbContext(options))
        {
            var targetReloaded = await verificationDb.Users.SingleAsync(x => x.Id == target.Id);
            Assert.False(targetReloaded.IsActive);

            var audit = await verificationDb.AuditLogs.SingleAsync(x => x.EntityId == target.Id);
            Assert.Equal("UserDeactivated", audit.Action);
        }

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.ToggleActiveAsync(actor.Id, actor.Id));

        Assert.Equal("Posledního aktivního správce nelze odebrat ani deaktivovat.", ex.Message);
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static ApplicationUser CreateUser(string id, string displayName, string email, bool isActive) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            IsActive = isActive,
            SecurityStamp = "initial-stamp",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

    private static void SeedRole(ApplicationDbContext db, string roleId, string roleName)
    {
        if (db.Roles.Any(x => x.Id == roleId))
        {
            return;
        }

        db.Roles.Add(new IdentityRole
        {
            Id = roleId,
            Name = roleName,
            NormalizedName = roleName.ToUpperInvariant()
        });
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
        private readonly DateTimeOffset _now = new(new DateTime(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc));

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
