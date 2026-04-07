using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Auth;

namespace RegistraceOvcina.Web.Tests;

public sealed class MagicLinkAuthServiceTests
{
    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task RequestMagicLink_CreatesToken_ForExistingUser()
    {
        using var db = CreateDb();
        var user = new ApplicationUser { Email = "test@example.com", UserName = "test@example.com", NormalizedEmail = "TEST@EXAMPLE.COM", EmailConfirmed = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new MagicLinkAuthService(db, TimeProvider.System);
        var result = await service.RequestMagicLinkAsync("test@example.com");

        Assert.NotNull(result);
        Assert.Equal("test@example.com", result.Email);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.True(result.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(59));
        Assert.Equal(user.Id, result.UserId);
    }

    [Fact]
    public async Task RequestMagicLink_CreatesToken_ForNewUser()
    {
        using var db = CreateDb();

        var service = new MagicLinkAuthService(db, TimeProvider.System);
        var result = await service.RequestMagicLinkAsync("new@example.com");

        Assert.NotNull(result);
        Assert.Equal("new@example.com", result.Email);
        Assert.Null(result.UserId);
    }

    [Fact]
    public async Task RequestMagicLink_ReturnsNull_WhenRateLimited()
    {
        using var db = CreateDb();

        var service = new MagicLinkAuthService(db, TimeProvider.System);
        await service.RequestMagicLinkAsync("flood@example.com");
        await service.RequestMagicLinkAsync("flood@example.com");
        await service.RequestMagicLinkAsync("flood@example.com");

        var result = await service.RequestMagicLinkAsync("flood@example.com");
        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyToken_ReturnsToken_WhenValid()
    {
        using var db = CreateDb();
        var service = new MagicLinkAuthService(db, TimeProvider.System);
        var created = await service.RequestMagicLinkAsync("test@example.com");

        var result = await service.VerifyTokenAsync(created!.Token);

        Assert.NotNull(result);
        Assert.Equal("test@example.com", result.Email);
        Assert.True(result.IsUsed);
    }

    [Fact]
    public async Task VerifyToken_ReturnsNull_WhenAlreadyUsed()
    {
        using var db = CreateDb();
        var service = new MagicLinkAuthService(db, TimeProvider.System);
        var created = await service.RequestMagicLinkAsync("test@example.com");

        await service.VerifyTokenAsync(created!.Token);
        var result = await service.VerifyTokenAsync(created.Token);

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyToken_ReturnsNull_WhenExpired()
    {
        using var db = CreateDb();
        var service = new MagicLinkAuthService(db, TimeProvider.System);
        var created = await service.RequestMagicLinkAsync("test@example.com");

        var token = await db.LoginTokens.FirstAsync(t => t.Token == created!.Token);
        token.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var result = await service.VerifyTokenAsync(created!.Token);
        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyToken_ReturnsNull_WhenNotFound()
    {
        using var db = CreateDb();
        var service = new MagicLinkAuthService(db, TimeProvider.System);

        var result = await service.VerifyTokenAsync("nonexistent-token");
        Assert.Null(result);
    }
}
