using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Tests.Features.Roles;

/// <summary>
/// Covers the v0.9.26 fix for the "Uživatelů" column on /admin/roles.
/// Before v0.9.26 the column always showed 0 because the query was filtered to the
/// currently-selected game. The new implementation counts distinct users ever assigned
/// a GameRoles row with a given RoleName, across any GameId.
/// </summary>
public sealed class RoleUserCountTests
{
    [Fact]
    public async Task DistinctUserCount_groups_by_rolename_across_all_games()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using (var db = new ApplicationDbContext(options))
        {
            // Two games, two users.
            // user-1 assigned "npc" in game 1 AND game 2 — should be counted once.
            // user-2 assigned "npc" in game 1 only — distinct.
            // user-2 assigned "helper" in game 2 — bumps helper count to 1.
            db.GameRoles.AddRange(
                new GameRole { UserId = "user-1", GameId = 1, RoleName = "npc",    AssignedAtUtc = At(1) },
                new GameRole { UserId = "user-1", GameId = 2, RoleName = "npc",    AssignedAtUtc = At(2) },
                new GameRole { UserId = "user-2", GameId = 1, RoleName = "npc",    AssignedAtUtc = At(3) },
                new GameRole { UserId = "user-2", GameId = 2, RoleName = "helper", AssignedAtUtc = At(4) }
            );
            await db.SaveChangesAsync();
        }

        await using var db2 = new ApplicationDbContext(options);

        var counts = await db2.GameRoles.AsNoTracking()
            .GroupBy(gr => gr.RoleName)
            .Select(g => new { Name = g.Key, Users = g.Select(x => x.UserId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Name, x => x.Users);

        Assert.Equal(2, counts["npc"]);      // user-1 + user-2 (user-1 across 2 games counts once)
        Assert.Equal(1, counts["helper"]);   // user-2 only
    }

    [Fact]
    public async Task DistinctUserCount_returns_zero_when_no_assignments()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var db = new ApplicationDbContext(options);

        var counts = await db.GameRoles.AsNoTracking()
            .GroupBy(gr => gr.RoleName)
            .Select(g => new { Name = g.Key, Users = g.Select(x => x.UserId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Name, x => x.Users);

        Assert.Empty(counts);
    }

    [Fact]
    public async Task DistinctUserCount_ignores_duplicate_assignments_for_same_user_same_role_same_game()
    {
        // GameRoleService normally prevents this but defend against stale rows anyway.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using (var db = new ApplicationDbContext(options))
        {
            db.GameRoles.AddRange(
                new GameRole { UserId = "user-1", GameId = 1, RoleName = "npc", AssignedAtUtc = At(1) },
                new GameRole { UserId = "user-1", GameId = 1, RoleName = "npc", AssignedAtUtc = At(2) }
            );
            await db.SaveChangesAsync();
        }

        await using var db2 = new ApplicationDbContext(options);

        var counts = await db2.GameRoles.AsNoTracking()
            .GroupBy(gr => gr.RoleName)
            .Select(g => new { Name = g.Key, Users = g.Select(x => x.UserId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Name, x => x.Users);

        Assert.Equal(1, counts["npc"]);
    }

    private static DateTime At(int hour) => new(2026, 4, 1, hour, 0, 0, DateTimeKind.Utc);
}
