using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Auth;

public sealed class MagicLinkAuthService(
    ApplicationDbContext db,
    TimeProvider timeProvider)
{
    private const int TokenExpiryMinutes = 60;
    private const int MaxRequestsPerWindow = 3;
    private const int RateLimitWindowMinutes = 15;

    public async Task<LoginToken?> RequestMagicLinkAsync(
        string email,
        CancellationToken ct = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        // Rate limiting: max 3 requests per email in 15 minutes
        var windowStart = nowUtc.AddMinutes(-RateLimitWindowMinutes);
        var recentCount = await db.LoginTokens
            .CountAsync(t => t.Email == normalizedEmail && t.CreatedAtUtc >= windowStart, ct);

        if (recentCount >= MaxRequestsPerWindow)
        {
            return null;
        }

        // Check if user exists
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail.ToUpperInvariant(), ct);

        var loginToken = new LoginToken
        {
            Email = normalizedEmail,
            UserId = user?.Id,
            Token = Guid.NewGuid().ToString("N"),
            ExpiresAtUtc = nowUtc.AddMinutes(TokenExpiryMinutes),
            CreatedAtUtc = nowUtc
        };

        db.LoginTokens.Add(loginToken);
        await db.SaveChangesAsync(ct);

        return loginToken;
    }

    public async Task<LoginToken?> VerifyTokenAsync(
        string token,
        CancellationToken ct = default)
    {
        var loginToken = await db.LoginTokens
            .FirstOrDefaultAsync(t => t.Token == token, ct);

        if (loginToken is null || loginToken.IsUsed || loginToken.ExpiresAtUtc < DateTime.UtcNow)
        {
            return null;
        }

        loginToken.IsUsed = true;
        await db.SaveChangesAsync(ct);

        return loginToken;
    }
}
