using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Security;

namespace RegistraceOvcina.Web.Features.Auth;

public sealed class GuestAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly GuestAuthOptions _options;
    private readonly TimeProvider _timeProvider;

    public GuestAuthService(
        UserManager<ApplicationUser> userManager,
        IOptions<GuestAuthOptions> options,
        TimeProvider timeProvider)
    {
        _userManager = userManager;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public bool IsEnabled => _options.Enabled && !string.IsNullOrEmpty(_options.PinHash);

    public bool VerifyPin(string pin)
    {
        if (!IsEnabled) return false;
        return BCrypt.Net.BCrypt.Verify(pin, _options.PinHash);
    }

    public async Task<ApplicationUser> FindOrCreateGuestAsync(string displayName)
    {
        // Normalize: trim, limit length
        var name = displayName.Trim();
        if (name.Length > 50) name = name[..50];

        // Create a deterministic email for the guest based on name
        var normalizedName = name.ToLowerInvariant().Replace(" ", "-");
        var guestEmail = $"guest-{normalizedName}@ovcina.local";

        var user = await _userManager.FindByEmailAsync(guestEmail);
        if (user is not null)
        {
            user.LastLoginAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            await _userManager.UpdateAsync(user);
            return user;
        }

        user = new ApplicationUser
        {
            UserName = guestEmail,
            Email = guestEmail,
            DisplayName = name,
            IsActive = true,
            EmailConfirmed = true, // Guests don't need email confirmation
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        };

        var result = await _userManager.CreateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to create guest user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        await _userManager.AddToRoleAsync(user, RoleNames.Guest);
        return user;
    }
}
