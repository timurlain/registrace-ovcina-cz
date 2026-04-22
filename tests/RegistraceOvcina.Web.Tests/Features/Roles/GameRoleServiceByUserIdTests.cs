using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Roles;

namespace RegistraceOvcina.Web.Tests.Features.Roles;

/// <summary>
/// Covers the UserId-based overloads added in v0.9.20 so that role assignment
/// works for accounts linked via ApplicationUser.PersonId even when Person.Email
/// is null or doesn't match ApplicationUser.NormalizedEmail.
/// </summary>
public sealed class GameRoleServiceByUserIdTests
{
    private const string ActorId = "actor-1";
    private const int GameId = 1;

    [Fact]
    public async Task AssignRoleByUserIdAsync_creates_role_and_audit_log()
    {
        var options = CreateOptions();
        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(CreateUser("user-1", "Eva", "eva@example.cz"));
            await db.SaveChangesAsync();
        }

        var service = new GameRoleService(new TestDbContextFactory(options));
        await service.AssignRoleByUserIdAsync("user-1", GameId, "NPC", ActorId);

        await using var verify = new ApplicationDbContext(options);
        var role = await verify.GameRoles.SingleAsync();
        Assert.Equal("user-1", role.UserId);
        Assert.Equal(GameId, role.GameId);
        Assert.Equal("npc", role.RoleName);

        var audit = await verify.AuditLogs.SingleAsync();
        Assert.Equal("GameRole", audit.EntityType);
        Assert.Equal("Assigned", audit.Action);
        Assert.Equal(ActorId, audit.ActorUserId);
    }

    [Fact]
    public async Task AssignRoleByUserIdAsync_is_idempotent_when_role_already_exists()
    {
        var options = CreateOptions();
        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(CreateUser("user-1", "Eva", "eva@example.cz"));
            db.GameRoles.Add(new GameRole
            {
                UserId = "user-1",
                GameId = GameId,
                RoleName = "npc",
                AssignedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var service = new GameRoleService(new TestDbContextFactory(options));
        await service.AssignRoleByUserIdAsync("user-1", GameId, "npc", ActorId);

        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(1, await verify.GameRoles.CountAsync());
        Assert.Equal(0, await verify.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task AssignRoleByUserIdAsync_throws_when_user_missing()
    {
        var options = CreateOptions();
        await using (var db = new ApplicationDbContext(options))
        {
            await db.SaveChangesAsync();
        }

        var service = new GameRoleService(new TestDbContextFactory(options));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AssignRoleByUserIdAsync("missing-user", GameId, "npc", ActorId));
    }

    [Fact]
    public async Task RevokeRoleByUserIdAsync_removes_role_and_writes_audit_log()
    {
        var options = CreateOptions();
        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(CreateUser("user-1", "Eva", "eva@example.cz"));
            db.GameRoles.Add(new GameRole
            {
                UserId = "user-1",
                GameId = GameId,
                RoleName = "npc",
                AssignedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var service = new GameRoleService(new TestDbContextFactory(options));
        await service.RevokeRoleByUserIdAsync("user-1", GameId, "npc", ActorId);

        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(0, await verify.GameRoles.CountAsync());
        var audit = await verify.AuditLogs.SingleAsync();
        Assert.Equal("Revoked", audit.Action);
    }

    [Fact]
    public async Task RevokeRoleByUserIdAsync_is_noop_when_role_not_present()
    {
        var options = CreateOptions();
        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(CreateUser("user-1", "Eva", "eva@example.cz"));
            await db.SaveChangesAsync();
        }

        var service = new GameRoleService(new TestDbContextFactory(options));
        await service.RevokeRoleByUserIdAsync("user-1", GameId, "npc", ActorId);

        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(0, await verify.AuditLogs.CountAsync());
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

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

    private static DateTime FixedDate() => new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ApplicationDbContext(options));
    }
}
