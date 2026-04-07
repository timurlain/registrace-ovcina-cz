# Magic Link Authentication Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace email+password auth with passwordless magic link + OAuth, using Azure Communication Services for transactional email.

**Architecture:** Add `LoginToken` entity to existing Identity schema. New `MagicLinkAuthService` handles token CRUD and rate limiting. New `AcsTransactionalEmailService` sends magic links via Azure Communication Services (separate from Graph inbox). Login page becomes email-first with OAuth buttons below. Password pages removed entirely.

**Tech Stack:** ASP.NET Core Identity (no passwords), EF Core + PostgreSQL, Azure.Communication.Email, xUnit

**Design doc:** `docs/plans/2026-04-07-magic-link-auth-design.md`

**Reference implementation:** Baca project at `C:\Users\TomášPajonk\source\repos\timurlain\baca` — `Services/AuthService.cs`, `Services/EmailService.cs`, `Models/LoginToken.cs`

---

### Task 1: Add LoginToken entity and migration

**Files:**
- Modify: `src/RegistraceOvcina.Web/Data/ApplicationModels.cs`
- Modify: `src/RegistraceOvcina.Web/Data/ApplicationDbContext.cs`
- Create: migration file (auto-generated)

**Step 1: Add LoginToken model**

Add to end of `ApplicationModels.cs`:

```csharp
public sealed class LoginToken
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string? UserId { get; set; }
    public string Token { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ApplicationUser? User { get; set; }
}
```

**Step 2: Add DbSet and configuration to ApplicationDbContext**

Add DbSet:
```csharp
public DbSet<LoginToken> LoginTokens => Set<LoginToken>();
```

Add to `OnModelCreating`:
```csharp
builder.Entity<LoginToken>(entity =>
{
    entity.HasKey(x => x.Id);
    entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
    entity.Property(x => x.Token).HasMaxLength(64).IsRequired();
    entity.HasIndex(x => x.Token).IsUnique();
    entity.HasIndex(x => new { x.Email, x.CreatedAtUtc });
    entity.HasOne(x => x.User)
        .WithMany()
        .HasForeignKey(x => x.UserId)
        .OnDelete(DeleteBehavior.SetNull);
});
```

**Step 3: Generate migration**

```bash
cd src/RegistraceOvcina.Web
dotnet ef migrations add AddLoginToken
```

Verify the migration file contains: new `LoginTokens` table, unique index on `Token`, composite index on `Email`+`CreatedAtUtc`.

**Step 4: Verify no pending model changes**

```bash
dotnet ef migrations has-pending-model-changes
```

Expected: "No changes have been made..."

**Step 5: Build**

```bash
dotnet build --no-restore
```

Expected: 0 errors, 0 warnings.

**Step 6: Commit**

```bash
git add src/RegistraceOvcina.Web/Data/ApplicationModels.cs \
        src/RegistraceOvcina.Web/Data/ApplicationDbContext.cs \
        src/RegistraceOvcina.Web/Migrations/
git commit -m "feat: add LoginToken entity for magic link auth"
```

---

### Task 2: Add AcsTransactionalEmailService

**Files:**
- Create: `src/RegistraceOvcina.Web/Features/Auth/AcsTransactionalEmailService.cs`
- Create: `src/RegistraceOvcina.Web/Features/Auth/AcsEmailOptions.cs`
- Modify: `src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj` (add NuGet)
- Modify: `src/RegistraceOvcina.Web/Program.cs` (register service + config)

**Step 1: Add Azure.Communication.Email NuGet**

```bash
cd src/RegistraceOvcina.Web
dotnet add package Azure.Communication.Email
```

**Step 2: Create AcsEmailOptions**

```csharp
namespace RegistraceOvcina.Web.Features.Auth;

public sealed class AcsEmailOptions
{
    public const string SectionName = "AzureCommunication";

    public string? ConnectionString { get; set; }
    public string? SenderAddress { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ConnectionString) &&
        !string.IsNullOrWhiteSpace(SenderAddress);
}
```

**Step 3: Create AcsTransactionalEmailService**

Reference Baca's `Services/EmailService.cs` for the ACS pattern.

```csharp
using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Options;

namespace RegistraceOvcina.Web.Features.Auth;

public sealed partial class AcsTransactionalEmailService(
    IOptions<AcsEmailOptions> options,
    ILogger<AcsTransactionalEmailService> logger)
{
    public async Task SendMagicLinkAsync(string recipientEmail, string magicLinkUrl, CancellationToken ct = default)
    {
        var config = options.Value;
        if (!config.IsConfigured)
        {
            logger.LogWarning("ACS not configured — magic link NOT sent to {Email}", recipientEmail);
            return;
        }

        var client = new EmailClient(config.ConnectionString);

        var emailMessage = new EmailMessage(
            senderAddress: config.SenderAddress,
            recipientAddress: recipientEmail,
            content: new EmailContent("Přihlášení — Ovčina registrace")
            {
                PlainText = $"""
                    Dobrý den,

                    Pro přihlášení do registrace Ovčina klikněte na následující odkaz:

                    {magicLinkUrl}

                    Odkaz je platný 60 minut. Pokud jste o přihlášení nežádali, tento e-mail ignorujte.

                    Ovčina registrace
                    """
            });

        await client.SendAsync(WaitUntil.Started, emailMessage, ct);
        LogMagicLinkSent(recipientEmail);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Magic link sent to {Email}")]
    private partial void LogMagicLinkSent(string email);
}
```

**Step 4: Register in Program.cs**

Add after the existing service registrations:

```csharp
builder.Services.Configure<AcsEmailOptions>(builder.Configuration.GetSection(AcsEmailOptions.SectionName));
builder.Services.AddScoped<AcsTransactionalEmailService>();
```

Add using: `using RegistraceOvcina.Web.Features.Auth;`

**Step 5: Build**

```bash
dotnet build --no-restore
```

**Step 6: Commit**

```bash
git add src/RegistraceOvcina.Web/Features/Auth/ \
        src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj \
        src/RegistraceOvcina.Web/Program.cs
git commit -m "feat: add ACS transactional email service for magic links"
```

---

### Task 3: Add MagicLinkAuthService

**Files:**
- Create: `src/RegistraceOvcina.Web/Features/Auth/MagicLinkAuthService.cs`
- Modify: `src/RegistraceOvcina.Web/Program.cs` (register)
- Create: `tests/RegistraceOvcina.Web.Tests/MagicLinkAuthServiceTests.cs`

**Step 1: Write failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
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
        var user = new ApplicationUser { Email = "test@example.com", UserName = "test@example.com", EmailConfirmed = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new MagicLinkAuthService(db, TimeProvider.System);
        var result = await service.RequestMagicLinkAsync("test@example.com");

        Assert.NotNull(result);
        Assert.Equal("test@example.com", result.Email);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.True(result.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(59));
    }

    [Fact]
    public async Task RequestMagicLink_CreatesToken_ForNewUser()
    {
        using var db = CreateDb();

        var service = new MagicLinkAuthService(db, TimeProvider.System);
        var result = await service.RequestMagicLinkAsync("new@example.com");

        Assert.NotNull(result);
        Assert.Equal("new@example.com", result.Email);
        Assert.Null(result.UserId); // user doesn't exist yet
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
        Assert.Null(result); // 4th request within 15 min
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

        await service.VerifyTokenAsync(created!.Token); // first use
        var result = await service.VerifyTokenAsync(created.Token); // second use

        Assert.Null(result);
    }

    [Fact]
    public async Task VerifyToken_ReturnsNull_WhenExpired()
    {
        using var db = CreateDb();
        // Create token, then manually expire it
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
```

**Step 2: Run tests to verify they fail**

```bash
dotnet test tests/RegistraceOvcina.Web.Tests --filter "MagicLinkAuthServiceTests" -v minimal
```

Expected: FAIL — `MagicLinkAuthService` does not exist.

**Step 3: Implement MagicLinkAuthService**

```csharp
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
```

**Step 4: Register in Program.cs**

```csharp
builder.Services.AddScoped<MagicLinkAuthService>();
```

**Step 5: Run tests**

```bash
dotnet test tests/RegistraceOvcina.Web.Tests --filter "MagicLinkAuthServiceTests" -v minimal
```

Expected: All 7 tests PASS.

**Step 6: Commit**

```bash
git add src/RegistraceOvcina.Web/Features/Auth/MagicLinkAuthService.cs \
        src/RegistraceOvcina.Web/Program.cs \
        tests/RegistraceOvcina.Web.Tests/MagicLinkAuthServiceTests.cs
git commit -m "feat: add MagicLinkAuthService with TDD tests"
```

---

### Task 4: Add magic link verification endpoint

**Files:**
- Modify: `src/RegistraceOvcina.Web/Components/Account/IdentityComponentsEndpointRouteBuilderExtensions.cs`
- Create: `src/RegistraceOvcina.Web/Components/Account/Pages/VerifyMagicLink.razor`

**Step 1: Add GET endpoint for magic link verification**

Add to `IdentityComponentsEndpointRouteBuilderExtensions.cs`, inside `MapAdditionalIdentityEndpoints`, after the Logout endpoint:

```csharp
accountGroup.MapGet("/VerifyMagicLink", async (
    [FromQuery] string token,
    [FromQuery] string? returnUrl,
    [FromServices] MagicLinkAuthService magicLinkService,
    [FromServices] UserManager<ApplicationUser> userManager,
    [FromServices] SignInManager<ApplicationUser> signInManager,
    [FromServices] TimeProvider timeProvider,
    HttpContext context) =>
{
    var loginToken = await magicLinkService.VerifyTokenAsync(token);
    if (loginToken is null)
    {
        return Results.Redirect("/Account/Login?error=invalid-token");
    }

    // Find or create user
    var user = loginToken.UserId is not null
        ? await userManager.FindByIdAsync(loginToken.UserId)
        : await userManager.FindByEmailAsync(loginToken.Email);

    if (user is null)
    {
        // Auto-create account on first magic link verification
        user = new ApplicationUser
        {
            UserName = loginToken.Email,
            Email = loginToken.Email,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };

        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            return Results.Redirect("/Account/Login?error=create-failed");
        }

        await userManager.AddToRoleAsync(user, RoleNames.Registrant);
    }

    user.LastLoginAtUtc = timeProvider.GetUtcNow().UtcDateTime;
    await userManager.UpdateAsync(user);

    await signInManager.SignInAsync(user, isPersistent: true);

    return Results.LocalRedirect(returnUrl ?? "~/");
}).AllowAnonymous();
```

Add usings at top of file:
```csharp
using RegistraceOvcina.Web.Features.Auth;
using RegistraceOvcina.Web.Security;
```

**Step 2: Build**

```bash
dotnet build --no-restore
```

**Step 3: Commit**

```bash
git add src/RegistraceOvcina.Web/Components/Account/
git commit -m "feat: add magic link verification endpoint with auto-create"
```

---

### Task 5: Rewrite Login page

**Files:**
- Modify: `src/RegistraceOvcina.Web/Components/Account/Pages/Login.razor`

**Step 1: Rewrite Login.razor**

Replace the entire login page with the email-first + OAuth layout. The page has two states:
- **Default**: email input + "Přihlásit se" button + OAuth buttons
- **Link sent**: confirmation message + "Odeslat znovu" button

Key changes:
- Remove password field entirely
- Remove "remember me" checkbox (magic link sessions are always persistent)
- Remove "Nemáte účet?" link (no registration page needed)
- Add magic link request via form POST
- Show success/error messages
- Keep external provider buttons (they already work)

The form POSTs to a new endpoint that calls `MagicLinkAuthService.RequestMagicLinkAsync` + `AcsTransactionalEmailService.SendMagicLinkAsync`.

**Step 2: Add magic link request endpoint**

In `IdentityComponentsEndpointRouteBuilderExtensions.cs`, add:

```csharp
accountGroup.MapPost("/RequestMagicLink", async (
    [FromForm] string email,
    [FromForm] string? returnUrl,
    [FromServices] MagicLinkAuthService magicLinkService,
    [FromServices] AcsTransactionalEmailService emailService,
    [FromServices] NavigationManager navigationManager,
    HttpContext context) =>
{
    var loginToken = await magicLinkService.RequestMagicLinkAsync(email.Trim());

    if (loginToken is not null)
    {
        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
        var verifyUrl = $"{baseUrl}/Account/VerifyMagicLink?token={loginToken.Token}";
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            verifyUrl += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
        }
        await emailService.SendMagicLinkAsync(loginToken.Email, verifyUrl);
    }

    // Always redirect to success (don't reveal whether email exists)
    return Results.Redirect($"/Account/Login?linkSent=1&returnUrl={Uri.EscapeDataString(returnUrl ?? "")}");
}).AllowAnonymous();
```

**Step 3: Build and verify**

```bash
dotnet build --no-restore
```

**Step 4: Commit**

```bash
git add src/RegistraceOvcina.Web/Components/Account/
git commit -m "feat: rewrite login page — email-first magic link + OAuth"
```

---

### Task 6: Configure session cookie

**Files:**
- Modify: `src/RegistraceOvcina.Web/Program.cs`

**Step 1: Update cookie configuration**

Find the existing `ConfigureApplicationCookie` block (around line 110) and update:

```csharp
builder.Services.ConfigureApplicationCookie(options =>
{
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});
```

**Step 2: Build**

```bash
dotnet build --no-restore
```

**Step 3: Commit**

```bash
git add src/RegistraceOvcina.Web/Program.cs
git commit -m "feat: 30-day sliding session cookie"
```

---

### Task 7: Remove password pages and clean up

**Files:**
- Delete: `src/RegistraceOvcina.Web/Components/Account/Pages/Register.razor`
- Delete: `src/RegistraceOvcina.Web/Components/Account/Pages/RegisterConfirmation.razor`
- Delete: `src/RegistraceOvcina.Web/Components/Account/Pages/ForgotPassword.razor`
- Delete: `src/RegistraceOvcina.Web/Components/Account/Pages/ForgotPasswordConfirmation.razor`
- Delete: `src/RegistraceOvcina.Web/Components/Account/Pages/ResetPassword.razor`
- Delete: `src/RegistraceOvcina.Web/Components/Account/Pages/ResetPasswordConfirmation.razor`
- Delete: `src/RegistraceOvcina.Web/Components/Account/Pages/InvalidPasswordReset.razor`
- Delete: `src/RegistraceOvcina.Web/Components/Account/Pages/LoginWith2fa.razor`
- Delete: `src/RegistraceOvcina.Web/Components/Account/Pages/LoginWithRecoveryCode.razor`
- Delete: `src/RegistraceOvcina.Web/Components/Account/Pages/Manage/ChangePassword.razor`
- Delete: `src/RegistraceOvcina.Web/Components/Account/Pages/Manage/SetPassword.razor`
- Delete: `src/RegistraceOvcina.Web/Components/Account/Pages/Manage/EnableAuthenticator.razor`
- Delete: `src/RegistraceOvcina.Web/Components/Account/Pages/Manage/Disable2fa.razor`
- Delete: `src/RegistraceOvcina.Web/Components/Account/Pages/Manage/GenerateRecoveryCodes.razor`
- Delete: `src/RegistraceOvcina.Web/Components/Account/Pages/Manage/ResetAuthenticator.razor`
- Delete: `src/RegistraceOvcina.Web/Components/Account/Pages/Manage/TwoFactorAuthentication.razor`
- Delete: `src/RegistraceOvcina.Web/Components/Account/Pages/ResendEmailConfirmation.razor`
- Delete: `src/RegistraceOvcina.Web/Components/Account/IdentityNoOpEmailSender.cs`
- Modify: `src/RegistraceOvcina.Web/Program.cs` — remove password options, remove NoOpEmailSender fallback

**Step 1: Delete password-related pages**

```bash
cd src/RegistraceOvcina.Web/Components/Account/Pages
rm -f Register.razor RegisterConfirmation.razor \
     ForgotPassword.razor ForgotPasswordConfirmation.razor \
     ResetPassword.razor ResetPasswordConfirmation.razor \
     InvalidPasswordReset.razor \
     LoginWith2fa.razor LoginWithRecoveryCode.razor \
     ResendEmailConfirmation.razor
cd Manage
rm -f ChangePassword.razor SetPassword.razor \
     EnableAuthenticator.razor Disable2fa.razor \
     GenerateRecoveryCodes.razor ResetAuthenticator.razor \
     TwoFactorAuthentication.razor
```

**Step 2: Delete NoOpEmailSender**

```bash
rm src/RegistraceOvcina.Web/Components/Account/IdentityNoOpEmailSender.cs
```

**Step 3: Clean up Program.cs**

- Remove password requirement options (lines with `options.Password.*`)
- Remove `IdentityNoOpEmailSender` registration and its `else` branch
- Keep `IEmailSender<ApplicationUser>` registration only for the Graph sender (used by Identity for email change confirmation)

**Step 4: Build — fix any broken references**

```bash
dotnet build --no-restore
```

Fix compilation errors from references to deleted pages (check `IdentityComponentsEndpointRouteBuilderExtensions.cs` usings, and `_Imports.razor`).

**Step 5: Commit**

```bash
git add -A
git commit -m "chore: remove password pages and NoOpEmailSender"
```

---

### Task 8: Populate DisplayName from first submission

**Files:**
- Modify: `src/RegistraceOvcina.Web/Features/Submissions/SubmissionService.cs`

**Step 1: Find `UpdateContactAsync` method**

Add `UserManager<ApplicationUser>` to the service constructor (or `IDbContextFactory` already covers Identity via the same DbContext).

At the end of `UpdateContactAsync`, after saving the submission, add:

```csharp
// Populate DisplayName on first contact save
var appUser = await db.Users.FindAsync([userId], cancellationToken);
if (appUser is not null && string.IsNullOrWhiteSpace(appUser.DisplayName))
{
    appUser.DisplayName = input.PrimaryContactName.Trim();
    await db.SaveChangesAsync(cancellationToken);
}
```

**Step 2: Build**

```bash
dotnet build --no-restore
```

**Step 3: Commit**

```bash
git add src/RegistraceOvcina.Web/Features/Submissions/SubmissionService.cs
git commit -m "feat: populate DisplayName from first submission contact info"
```

---

### Task 9: Update E2E test login helper

**Files:**
- Modify: `tests/RegistraceOvcina.E2E/AppFixture.cs` (or wherever the test login helper is)

**Step 1: Check the existing testing login endpoint**

The app already has a `/testing/login` endpoint for E2E tests (seen in Program.cs). Verify it still works — it bypasses auth by directly signing in with a test email. This should keep working since it uses `SignInManager` directly, not passwords.

**Step 2: Verify E2E tests pass**

```bash
dotnet test tests/RegistraceOvcina.E2E -v minimal
```

**Step 3: If tests fail, fix the test helper**

The testing endpoint takes `?email=` and signs in directly — this should not be affected by the password removal. If it is, update it to match the new flow.

**Step 4: Commit if changes needed**

```bash
git add tests/
git commit -m "fix: update E2E test login for passwordless auth"
```

---

### Task 10: Version bump and final verification

**Files:**
- Modify: `src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj`

**Step 1: Bump version**

Already bumped to 0.6.10 for the hotfix. If this goes as a separate PR, bump to 0.7.0 (breaking auth change).

**Step 2: Full build**

```bash
dotnet build
```

**Step 3: Run all unit tests**

```bash
dotnet test tests/RegistraceOvcina.Web.Tests -v minimal
```

**Step 4: Commit**

```bash
git add src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj
git commit -m "release: v0.7.0 — magic link authentication"
```

---

## Configuration Required for Deployment

Add to Azure Container App environment variables / GitHub secrets:

```
AzureCommunication__ConnectionString=endpoint=https://...
AzureCommunication__SenderAddress=DoNotReply@{acs-domain}.azurecomm.net
```

The ACS resource can be the same one used by Baca if the sender domain is shared, or a new one specific to Ovčina.

## Checklist Before Merge

- [ ] All unit tests pass
- [ ] E2E tests pass
- [ ] Login page renders correctly (email form + OAuth buttons)
- [ ] Magic link sends via ACS (test with real email)
- [ ] Magic link verification creates account and signs in
- [ ] Existing OAuth logins still work
- [ ] Session persists across browser restart (30-day cookie)
- [ ] Rate limiting works (4th request in 15 min returns same success page)
- [ ] Password pages return 404 or redirect
- [ ] DisplayName populates on first submission
- [ ] No migration issues on fresh DB
- [ ] ACS config missing → graceful degradation (log warning, don't crash)
