using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Users;

namespace RegistraceOvcina.Web.Tests;

public sealed class UserEmailServiceTests
{
    [Fact]
    public async Task AddAlternateEmail_Succeeds()
    {
        var options = CreateOptions();
        var user = CreateUser("user-1", "Alice", "alice@example.cz", true);

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var service = new UserEmailService(new TestDbContextFactory(options), new FixedTimeProvider());

        await service.AddAlternateEmailAsync("user-1", "alice-alt@example.cz");

        await using var verificationDb = new ApplicationDbContext(options);
        var saved = await verificationDb.UserEmails.SingleAsync();

        Assert.Equal("user-1", saved.UserId);
        Assert.Equal("alice-alt@example.cz", saved.Email);
        Assert.Equal("ALICE-ALT@EXAMPLE.CZ", saved.NormalizedEmail);
    }

    [Fact]
    public async Task AddAlternateEmail_RejectsDuplicate_InUsers()
    {
        var options = CreateOptions();
        var user1 = CreateUser("user-1", "Alice", "alice@example.cz", true);
        var user2 = CreateUser("user-2", "Bob", "bob@example.cz", true);

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.AddRange(user1, user2);
            await db.SaveChangesAsync();
        }

        var service = new UserEmailService(new TestDbContextFactory(options), new FixedTimeProvider());

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.AddAlternateEmailAsync("user-1", "bob@example.cz"));

        Assert.Equal("Tento e-mail je již přiřazen jinému účtu.", ex.Message);
    }

    [Fact]
    public async Task AddAlternateEmail_RejectsDuplicate_InUserEmails()
    {
        var options = CreateOptions();
        var user1 = CreateUser("user-1", "Alice", "alice@example.cz", true);
        var user2 = CreateUser("user-2", "Bob", "bob@example.cz", true);

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.AddRange(user1, user2);
            db.UserEmails.Add(new UserEmail
            {
                UserId = "user-2",
                Email = "shared@example.cz",
                NormalizedEmail = "SHARED@EXAMPLE.CZ",
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
            await db.SaveChangesAsync();
        }

        var service = new UserEmailService(new TestDbContextFactory(options), new FixedTimeProvider());

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.AddAlternateEmailAsync("user-1", "shared@example.cz"));

        Assert.Equal("Tento e-mail je již přiřazen jinému účtu.", ex.Message);
    }

    [Fact]
    public async Task AddAlternateEmail_RejectsSameAsPrimary()
    {
        var options = CreateOptions();
        var user = CreateUser("user-1", "Alice", "alice@example.cz", true);

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var service = new UserEmailService(new TestDbContextFactory(options), new FixedTimeProvider());

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.AddAlternateEmailAsync("user-1", "alice@example.cz"));

        Assert.Equal("Alternativní e-mail nesmí být stejný jako primární.", ex.Message);
    }

    [Fact]
    public async Task AddAlternateEmail_RejectsOverLimit()
    {
        var options = CreateOptions();
        var user = CreateUser("user-1", "Alice", "alice@example.cz", true);

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(user);

            for (var i = 1; i <= 4; i++)
            {
                db.UserEmails.Add(new UserEmail
                {
                    UserId = "user-1",
                    Email = $"alt{i}@example.cz",
                    NormalizedEmail = $"ALT{i}@EXAMPLE.CZ",
                    CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                });
            }

            await db.SaveChangesAsync();
        }

        var service = new UserEmailService(new TestDbContextFactory(options), new FixedTimeProvider());

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.AddAlternateEmailAsync("user-1", "alt5@example.cz"));

        Assert.Equal("Uživatel může mít maximálně 4 alternativní e-maily.", ex.Message);
    }

    [Fact]
    public async Task RemoveAlternateEmail_Succeeds()
    {
        var options = CreateOptions();
        var user = CreateUser("user-1", "Alice", "alice@example.cz", true);

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(user);
            db.UserEmails.Add(new UserEmail
            {
                Id = 42,
                UserId = "user-1",
                Email = "alt@example.cz",
                NormalizedEmail = "ALT@EXAMPLE.CZ",
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
            await db.SaveChangesAsync();
        }

        var service = new UserEmailService(new TestDbContextFactory(options), new FixedTimeProvider());

        await service.RemoveAlternateEmailAsync("user-1", 42);

        await using var verificationDb = new ApplicationDbContext(options);
        Assert.Empty(await verificationDb.UserEmails.ToListAsync());
    }

    [Fact]
    public async Task RemoveAlternateEmail_WrongUser_DoesNothing()
    {
        var options = CreateOptions();
        var user1 = CreateUser("user-1", "Alice", "alice@example.cz", true);
        var user2 = CreateUser("user-2", "Bob", "bob@example.cz", true);

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.AddRange(user1, user2);
            db.UserEmails.Add(new UserEmail
            {
                Id = 42,
                UserId = "user-2",
                Email = "alt@example.cz",
                NormalizedEmail = "ALT@EXAMPLE.CZ",
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
            await db.SaveChangesAsync();
        }

        var service = new UserEmailService(new TestDbContextFactory(options), new FixedTimeProvider());

        await service.RemoveAlternateEmailAsync("user-1", 42);

        await using var verificationDb = new ApplicationDbContext(options);
        Assert.Single(await verificationDb.UserEmails.ToListAsync());
    }

    [Fact]
    public async Task ResolveUserIdByEmail_FindsPrimary()
    {
        var options = CreateOptions();
        var user = CreateUser("user-1", "Alice", "alice@example.cz", true);

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var service = new UserEmailService(new TestDbContextFactory(options), new FixedTimeProvider());

        var result = await service.ResolveUserIdByEmailAsync("alice@example.cz");

        Assert.Equal("user-1", result);
    }

    [Fact]
    public async Task ResolveUserIdByEmail_FindsAlternate()
    {
        var options = CreateOptions();
        var user = CreateUser("user-1", "Alice", "alice@example.cz", true);

        await using (var db = new ApplicationDbContext(options))
        {
            db.Users.Add(user);
            db.UserEmails.Add(new UserEmail
            {
                UserId = "user-1",
                Email = "alice-alt@example.cz",
                NormalizedEmail = "ALICE-ALT@EXAMPLE.CZ",
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
            await db.SaveChangesAsync();
        }

        var service = new UserEmailService(new TestDbContextFactory(options), new FixedTimeProvider());

        var result = await service.ResolveUserIdByEmailAsync("alice-alt@example.cz");

        Assert.Equal("user-1", result);
    }

    [Fact]
    public async Task ResolveUserIdByEmail_ReturnsNull_WhenNotFound()
    {
        var options = CreateOptions();

        await using (var db = new ApplicationDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
        }

        var service = new UserEmailService(new TestDbContextFactory(options), new FixedTimeProvider());

        var result = await service.ResolveUserIdByEmailAsync("nobody@example.cz");

        Assert.Null(result);
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
