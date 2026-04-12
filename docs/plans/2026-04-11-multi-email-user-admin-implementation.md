# Multi-Email Linking & User Admin — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Let organizers link up to 4 alternate emails to a user, so integration API lookups and logins work with any of them. Add a user admin page with quick email lookup.

**Architecture:** New `UserEmail` linking table, shared `ResolveUserByEmail` helper used by integration API + login flow, extended `UserAdministrationService` for admin page.

**Tech Stack:** .NET 10, Blazor Server, EF Core + PostgreSQL, xUnit, ASP.NET Identity

**Design doc:** `docs/plans/2026-04-11-multi-email-user-admin-design.md`

---

### Task 1: UserEmail entity + EF configuration + migration

**Files:**
- Modify: `src/RegistraceOvcina.Web/Data/ApplicationModels.cs` (add UserEmail class)
- Modify: `src/RegistraceOvcina.Web/Data/ApplicationDbContext.cs` (add DbSet + OnModelCreating config)
- Modify: `src/RegistraceOvcina.Web/Data/ApplicationUser.cs` (add navigation property)
- Create: migration via `dotnet ef migrations add AddUserEmails`

**Step 1: Add UserEmail entity to ApplicationModels.cs**

At the end of the file, add:

```csharp
public sealed class UserEmail
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public ApplicationUser User { get; set; } = default!;
}
```

**Step 2: Add navigation property to ApplicationUser**

In `ApplicationUser.cs`, add:

```csharp
public List<UserEmail> AlternateEmails { get; set; } = [];
```

**Step 3: Add DbSet + EF configuration to ApplicationDbContext**

Add DbSet:
```csharp
public DbSet<UserEmail> UserEmails => Set<UserEmail>();
```

In `OnModelCreating`, add:
```csharp
modelBuilder.Entity<UserEmail>(entity =>
{
    entity.HasIndex(x => x.NormalizedEmail).IsUnique();
    entity.HasIndex(x => x.UserId);
    entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
    entity.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
    entity.HasOne(x => x.User)
        .WithMany(x => x.AlternateEmails)
        .HasForeignKey(x => x.UserId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

**Step 4: Generate migration**

```bash
cd src/RegistraceOvcina.Web
dotnet ef migrations add AddUserEmails
```

Verify the migration contains: CreateTable for UserEmails, unique index on NormalizedEmail, FK to AspNetUsers.

**Step 5: Verify no pending model changes**

```bash
dotnet ef migrations has-pending-model-changes
```

Expected: "No changes have been found"

**Step 6: Commit**

```bash
git add -A && git commit -m "feat: add UserEmail entity with EF configuration and migration"
```

---

### Task 2: UserEmailService — CRUD + validation

**Files:**
- Create: `src/RegistraceOvcina.Web/Features/Users/UserEmailService.cs`
- Modify: `src/RegistraceOvcina.Web/Program.cs` (register service)

**Step 1: Create UserEmailService**

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Users;

public sealed class UserEmailService(IDbContextFactory<ApplicationDbContext> dbFactory, TimeProvider timeProvider)
{
    public const int MaxAlternateEmails = 4;

    public async Task<List<UserEmail>> GetAlternateEmailsAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.UserEmails
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Email)
            .ToListAsync(ct);
    }

    public async Task AddAlternateEmailAsync(string userId, string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ValidationException("E-mail je povinný.");

        var normalizedEmail = email.Trim().ToUpperInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Check max count
        var currentCount = await db.UserEmails.CountAsync(x => x.UserId == userId, ct);
        if (currentCount >= MaxAlternateEmails)
            throw new ValidationException($"Uživatel může mít maximálně {MaxAlternateEmails} alternativní e-maily.");

        // Check not same as primary
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw new ValidationException("Uživatel nenalezen.");
        if (string.Equals(user.NormalizedEmail, normalizedEmail, StringComparison.Ordinal))
            throw new ValidationException("Alternativní e-mail nesmí být stejný jako primární.");

        // Check uniqueness across AspNetUsers and UserEmails
        var existsInUsers = await db.Users.AnyAsync(x => x.NormalizedEmail == normalizedEmail, ct);
        if (existsInUsers)
            throw new ValidationException("Tento e-mail je již přiřazen jinému účtu.");

        var existsInAlternates = await db.UserEmails.AnyAsync(x => x.NormalizedEmail == normalizedEmail, ct);
        if (existsInAlternates)
            throw new ValidationException("Tento e-mail je již přiřazen jinému účtu.");

        db.UserEmails.Add(new UserEmail
        {
            UserId = userId,
            Email = email.Trim(),
            NormalizedEmail = normalizedEmail,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAlternateEmailAsync(string userId, int emailId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entry = await db.UserEmails
            .SingleOrDefaultAsync(x => x.Id == emailId && x.UserId == userId, ct);
        if (entry is not null)
        {
            db.UserEmails.Remove(entry);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Resolves a user ID by checking primary email first, then alternate emails.
    /// Returns null if no user found.
    /// </summary>
    public async Task<string?> ResolveUserIdByEmailAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var normalizedEmail = email.Trim().ToUpperInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Primary email first
        var userId = await db.Users
            .AsNoTracking()
            .Where(x => x.NormalizedEmail == normalizedEmail)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (userId is not null) return userId;

        // Alternate email fallback
        return await db.UserEmails
            .AsNoTracking()
            .Where(x => x.NormalizedEmail == normalizedEmail)
            .Select(x => x.UserId)
            .FirstOrDefaultAsync(ct);
    }
}
```

**Step 2: Register in Program.cs**

Find the service registrations section and add:
```csharp
builder.Services.AddScoped<UserEmailService>();
```

**Step 3: Commit**

```bash
git add -A && git commit -m "feat: add UserEmailService with CRUD, validation, and email resolution"
```

---

### Task 3: UserEmailService unit tests

**Files:**
- Create: `tests/RegistraceOvcina.Web.Tests/UserEmailServiceTests.cs`

Follow the same InMemory SQLite pattern as `UserAdministrationServiceTests.cs` — use `CreateOptions()`, `TestDbContextFactory`, `CreateUser()`, `FixedTimeProvider()`.

**Tests to write:**

1. `AddAlternateEmail_Succeeds` — add one email, verify it's persisted with correct NormalizedEmail
2. `AddAlternateEmail_RejectsDuplicate_InUsers` — fail when email exists in AspNetUsers
3. `AddAlternateEmail_RejectsDuplicate_InUserEmails` — fail when email exists in UserEmails
4. `AddAlternateEmail_RejectsSameAsPrimary` — fail when email matches user's own primary
5. `AddAlternateEmail_RejectsOverLimit` — fail when user already has 4
6. `RemoveAlternateEmail_Succeeds` — remove existing, verify gone
7. `RemoveAlternateEmail_WrongUser_DoesNothing` — won't remove another user's email
8. `ResolveUserIdByEmail_FindsPrimary` — resolves via AspNetUsers
9. `ResolveUserIdByEmail_FindsAlternate` — resolves via UserEmails
10. `ResolveUserIdByEmail_ReturnsNull_WhenNotFound` — no match returns null

**Step 1: Write all tests**

Use the helpers from `UserAdministrationServiceTests.cs` (bottom of that file has `CreateOptions`, `CreateUser`, `SeedRole`, `TestDbContextFactory`, `FixedTimeProvider`). Either reference them or duplicate — check if they're in a shared location.

**Step 2: Run tests**

```bash
dotnet test tests/RegistraceOvcina.Web.Tests --filter "FullyQualifiedName~UserEmailService" -v n
```

Expected: all 10 pass.

**Step 3: Commit**

```bash
git add -A && git commit -m "test: add UserEmailService unit tests"
```

---

### Task 4: Integration API — use email resolution

**Files:**
- Modify: `src/RegistraceOvcina.Web/Features/Integration/IntegrationApiEndpoints.cs`
- Modify: `src/RegistraceOvcina.Web/Features/Roles/GameRoleService.cs`

**Step 1: Update `/users/by-email` endpoint**

Replace the direct `db.Users.Where(u => u.NormalizedEmail == normalizedEmail)` lookup with resolution through `UserEmailService.ResolveUserIdByEmailAsync`. Inject `UserEmailService` into the endpoint delegate.

```csharp
group.MapGet("/users/by-email", async (
    string email,
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserEmailService userEmailService,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(email))
        return Results.BadRequest("email is required.");

    var userId = await userEmailService.ResolveUserIdByEmailAsync(email, ct);
    if (userId is null)
        return Results.Ok(new { Exists = false, DisplayName = (string?)null, Roles = (List<string>?)null });

    await using var db = await dbFactory.CreateDbContextAsync(ct);
    var user = await db.Users.AsNoTracking()
        .Where(u => u.Id == userId && u.IsActive)
        .Select(u => new { u.DisplayName })
        .FirstOrDefaultAsync(ct);

    if (user is null)
        return Results.Ok(new { Exists = false, DisplayName = (string?)null, Roles = (List<string>?)null });

    var roles = await db.UserRoles.AsNoTracking()
        .Where(ur => ur.UserId == userId)
        .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
        .ToListAsync(ct);

    return Results.Ok(new { Exists = true, user.DisplayName, Roles = roles });
}).AllowAnonymous();
```

**Step 2: Update `/registrations/check` endpoint**

After the existing Person.Email check, also check via UserEmailService → find user → find their PersonId → check registrations:

```csharp
group.MapGet("/registrations/check", async (
    string email,
    int gameId,
    IDbContextFactory<ApplicationDbContext> dbFactory,
    UserEmailService userEmailService,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(email))
        return Results.BadRequest("email is required.");

    await using var db = await dbFactory.CreateDbContextAsync(ct);
    var normalizedEmail = email.Trim().ToUpperInvariant();

    // Direct Person.Email match (existing behavior)
    var isRegistered = await db.Registrations.AsNoTracking()
        .AnyAsync(r =>
            r.Submission.GameId == gameId &&
            r.Submission.Status == SubmissionStatus.Submitted &&
            r.Status == RegistrationStatus.Active &&
            !r.Submission.IsDeleted &&
            r.Person.Email != null &&
            r.Person.Email.ToUpper() == normalizedEmail, ct);

    // Fallback: resolve via alternate emails → find linked Person
    if (!isRegistered)
    {
        var userId = await userEmailService.ResolveUserIdByEmailAsync(email, ct);
        if (userId is not null)
        {
            var personId = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.PersonId)
                .FirstOrDefaultAsync(ct);

            if (personId.HasValue)
            {
                isRegistered = await db.Registrations.AsNoTracking()
                    .AnyAsync(r =>
                        r.Submission.GameId == gameId &&
                        r.Submission.Status == SubmissionStatus.Submitted &&
                        r.Status == RegistrationStatus.Active &&
                        !r.Submission.IsDeleted &&
                        r.PersonId == personId.Value, ct);
            }
        }
    }

    return Results.Ok(new PresenceCheckDto(isRegistered));
}).AllowAnonymous();
```

**Step 3: Update GameRoleService to accept userId resolution**

Add an overload or modify `GetRolesForUserAsync` and `HasRoleAsync` to also check `UserEmails`:

```csharp
public async Task<List<string>> GetRolesForUserAsync(string email, int gameId)
{
    await using var db = await dbFactory.CreateDbContextAsync();
    var normalizedEmail = email.Trim().ToUpperInvariant();

    // Try primary email
    var userId = await db.Users.AsNoTracking()
        .Where(u => u.NormalizedEmail == normalizedEmail)
        .Select(u => u.Id)
        .FirstOrDefaultAsync();

    // Fallback to alternate
    userId ??= await db.UserEmails.AsNoTracking()
        .Where(ue => ue.NormalizedEmail == normalizedEmail)
        .Select(ue => ue.UserId)
        .FirstOrDefaultAsync();

    if (userId is null) return [];

    return await db.GameRoles.AsNoTracking()
        .Where(gr => gr.UserId == userId && gr.GameId == gameId)
        .Select(gr => gr.RoleName)
        .ToListAsync();
}
```

Apply the same pattern to `HasRoleAsync`.

**Step 4: Commit**

```bash
git add -A && git commit -m "feat: integration API resolves users by primary + alternate emails"
```

---

### Task 5: User admin page — quick lookup + alternate email management

**Files:**
- Create: `src/RegistraceOvcina.Web/Components/Pages/Admin/UserAdmin.razor` (new page at `/admin/uzivatele`)
- Modify: `src/RegistraceOvcina.Web/Features/Users/UserAdministrationService.cs` (add lookup method)
- Modify: `src/RegistraceOvcina.Web/Components/Layout/NavMenu.razor` (add nav link)

**Step 1: Add lookup method to UserAdministrationService**

```csharp
public async Task<UserLookupResult?> LookupByEmailAsync(string email, CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(email)) return null;
    var normalizedEmail = email.Trim().ToUpperInvariant();
    await using var db = await dbContextFactory.CreateDbContextAsync(ct);

    // Find user by primary or alternate email
    var userId = await db.Users.AsNoTracking()
        .Where(u => u.NormalizedEmail == normalizedEmail)
        .Select(u => u.Id)
        .FirstOrDefaultAsync(ct);

    userId ??= await db.UserEmails.AsNoTracking()
        .Where(ue => ue.NormalizedEmail == normalizedEmail)
        .Select(ue => ue.UserId)
        .FirstOrDefaultAsync(ct);

    if (userId is null) return null;

    var user = await db.Users.AsNoTracking()
        .Where(u => u.Id == userId)
        .Select(u => new { u.Id, u.DisplayName, u.Email, u.IsActive, u.LastLoginAtUtc, u.CreatedAtUtc })
        .SingleAsync(ct);

    var identityRoles = await db.UserRoles.AsNoTracking()
        .Where(ur => ur.UserId == userId)
        .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
        .ToListAsync(ct);

    var alternateEmails = await db.UserEmails.AsNoTracking()
        .Where(ue => ue.UserId == userId)
        .OrderBy(ue => ue.Email)
        .ToListAsync(ct);

    var gameRoles = await db.GameRoles.AsNoTracking()
        .Where(gr => gr.UserId == userId)
        .Include(gr => gr.Game)
        .Select(gr => new { gr.Game.Name, gr.RoleName, gr.GameId })
        .ToListAsync(ct);

    var registrations = await db.Registrations.AsNoTracking()
        .Where(r => r.Person.Email != null &&
            r.Person.Email.ToUpper() == normalizedEmail)
        .Include(r => r.Submission).ThenInclude(s => s.Game)
        .Select(r => new { GameName = r.Submission.Game.Name, r.Submission.Status, r.Status })
        .ToListAsync(ct);

    // Also check via PersonId link
    var personId = await db.Users.AsNoTracking()
        .Where(u => u.Id == userId)
        .Select(u => u.PersonId)
        .FirstOrDefaultAsync(ct);

    if (personId.HasValue && registrations.Count == 0)
    {
        registrations = await db.Registrations.AsNoTracking()
            .Where(r => r.PersonId == personId.Value)
            .Include(r => r.Submission).ThenInclude(s => s.Game)
            .Select(r => new { GameName = r.Submission.Game.Name, r.Submission.Status, r.Status })
            .ToListAsync(ct);
    }

    return new UserLookupResult(
        user.Id, user.DisplayName, user.Email ?? "",
        user.IsActive, user.LastLoginAtUtc,
        identityRoles, alternateEmails,
        gameRoles.Select(g => new GameRoleSummary(g.GameId, g.Name, g.RoleName)).ToList(),
        registrations.Select(r => new RegistrationSummary(r.GameName, r.Status.ToString(), r.Status.ToString())).ToList());
}
```

Add the DTOs at the bottom of `UserAdministrationService.cs`:

```csharp
public sealed record UserLookupResult(
    string Id, string DisplayName, string PrimaryEmail,
    bool IsActive, DateTime? LastLoginAtUtc,
    List<string> IdentityRoles, List<UserEmail> AlternateEmails,
    List<GameRoleSummary> GameRoles, List<RegistrationSummary> Registrations);

public sealed record GameRoleSummary(int GameId, string GameName, string RoleName);
public sealed record RegistrationSummary(string GameName, string SubmissionStatus, string RegistrationStatus);
```

**Step 2: Create UserAdmin.razor page**

Route: `@page "/admin/uzivatele"`
Authorization: `@attribute [Authorize(Policy = AuthorizationPolicies.AdminOnly)]`

Layout:
- Quick lookup input at top (email textbox + "Hledat" button)
- Result card showing: display name, primary email, alternate emails (with add/remove), identity roles, game roles, registration status
- Add alternate email: input + "Pridat" button, inline validation errors
- Remove alternate email: X button next to each

Follow the same card/shadow-sm pattern as `UserManagement.razor`.

**Step 3: Add nav link**

In `NavMenu.razor`, add a link to `/admin/uzivatele` labeled "Uživatelé" in the admin section.

**Step 4: Commit**

```bash
git add -A && git commit -m "feat: user admin page with quick lookup and alternate email management"
```

---

### Task 6: External login — alternate email confirmation flow

**Files:**
- Modify: `src/RegistraceOvcina.Web/Components/Account/Pages/ExternalLogin.razor`

**Step 1: Add alternate email check in OnLoginCallbackAsync**

In the `OnLoginCallbackAsync` method, after the existing `FindByEmailAsync` check (which auto-links if a user exists by primary email), add a check for alternate emails:

```csharp
// After existing "auto-link by primary email" block, before showing registration form:

// Check alternate emails — require confirmation before linking
if (!string.IsNullOrEmpty(email))
{
    var userEmailService = HttpContext.RequestServices.GetRequiredService<UserEmailService>();
    var alternateUserId = await userEmailService.ResolveUserIdByEmailAsync(email);
    if (alternateUserId is not null)
    {
        var alternateUser = await UserManager.FindByIdAsync(alternateUserId);
        if (alternateUser is not null)
        {
            // Store the pending link info and show confirmation UI
            pendingLinkUser = alternateUser;
            return; // Razor will render the confirmation section
        }
    }
}
```

**Step 2: Add confirmation UI section to the razor markup**

Before the existing registration form, add:

```razor
@if (pendingLinkUser is not null)
{
    <div class="alert alert-info">
        <h4>Nalezen existující účet</h4>
        <p>Našli jsme účet <strong>@pendingLinkUser.DisplayName</strong> propojený
           s e-mailovou adresou <strong>@email</strong>. Je to váš účet?</p>
    </div>
    <div class="d-flex gap-2">
        <form @formname="confirm-link" @onsubmit="OnConfirmLinkAsync" method="post">
            <AntiforgeryToken />
            <button type="submit" class="btn btn-primary">Ano, to jsem já</button>
        </form>
        <form @formname="deny-link" @onsubmit="OnDenyLinkAsync" method="post">
            <AntiforgeryToken />
            <button type="submit" class="btn btn-outline-secondary">Ne, vytvořit nový účet</button>
        </form>
    </div>
}
```

**Step 3: Add confirm/deny handlers**

```csharp
private async Task OnConfirmLinkAsync()
{
    if (pendingLinkUser is not null && externalLoginInfo is not null)
    {
        var result = await UserManager.AddLoginAsync(pendingLinkUser, externalLoginInfo);
        if (result.Succeeded)
        {
            Logger.LogInformation("Linked {Provider} to existing account {Email} via alternate email.",
                externalLoginInfo.LoginProvider, pendingLinkUser.Email);
            await SignInManager.SignInAsync(pendingLinkUser, isPersistent: false);
            RedirectManager.RedirectTo(ReturnUrl);
            return;
        }
    }
    RedirectManager.RedirectToWithStatus("Account/Login",
        "Nepodařilo se propojit účet. Zkuste to znovu.", HttpContext);
}

private async Task OnDenyLinkAsync()
{
    pendingLinkUser = null;
    // Fall through to normal registration form
}
```

**Step 4: Add field declaration**

```csharp
private ApplicationUser? pendingLinkUser;
private string? email;
```

Make sure `email` is set from `externalLoginInfo.Principal.FindFirstValue(ClaimTypes.Email)` at the top of `OnLoginCallbackAsync`.

**Step 5: Commit**

```bash
git add -A && git commit -m "feat: external login confirms before linking via alternate email"
```

---

### Task 7: Version bump + final verification

**Files:**
- Modify: `src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj` (bump version to 0.8.2)

**Step 1: Bump version**

Change `<Version>0.8.1</Version>` to `<Version>0.8.2</Version>`.

**Step 2: Run all tests**

```bash
dotnet test tests/RegistraceOvcina.Web.Tests -v n
```

Expected: all tests pass including new UserEmailService tests.

**Step 3: Commit**

```bash
git add -A && git commit -m "[v0.8.2] feat: multi-email linking and user admin page"
```

**Step 4: Create PR**

```bash
gh pr create --title "[v0.8.2] feat: multi-email linking and user admin page" --body "..."
```

Wait for CI. Do NOT merge without user approval.
