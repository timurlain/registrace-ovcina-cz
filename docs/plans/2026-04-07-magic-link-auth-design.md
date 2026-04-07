# Magic Link Authentication — Design

**Date:** 2026-04-07
**Status:** Approved

## Problem

The app currently uses email+password registration with no email confirmation (`RequireConfirmedAccount = false`). Password resets go to a NoOp sender. Users without Microsoft/Google/Seznam (university emails, ISP emails, privacy-conscious users) need a reliable way to log in without remembering a password.

## Decision

Replace email+password with passwordless magic link authentication. Keep OAuth providers (Microsoft, Google, Seznam) as shortcuts. Use Azure Communication Services for transactional email delivery to avoid polluting the shared mailbox inbox.

## Login Flow

1. User enters email on the login page, clicks "Přihlásit se"
2. Backend creates a `LoginToken` (GUID, 60-minute expiry, one-time use)
3. If no `ApplicationUser` exists for this email, one is created with the `Registrant` role
4. ACS sends an email: "Pro přihlášení klikněte na odkaz níže" with the magic link
5. User clicks link → `GET /Account/VerifyMagicLink?token={guid}`
6. Backend validates token (exists, not used, not expired), marks it as used
7. Signs in via `SignInManager.SignInAsync`, sets session cookie
8. Redirect to return URL or home

## Login Page Layout

```
┌─────────────────────────────────┐
│        Přihlášení                │
│                                  │
│  E-mail: [________________]     │
│  [    Přihlásit se    ]         │
│                                  │
│  ─── nebo se přihlaste přes ─── │
│                                  │
│  [Google] [Seznam] [Microsoft]  │
└─────────────────────────────────┘
```

After submitting email:
```
┌─────────────────────────────────┐
│  Odkaz k přihlášení byl         │
│  odeslán na váš e-mail.         │
│  Odkaz je platný 60 minut.      │
│                                  │
│  [Odeslat znovu]                │
└─────────────────────────────────┘
```

## No Register Page

- First magic link click auto-creates the account with `Registrant` role
- `DisplayName` stays null until first submission, where `PrimaryContactName` is copied to `DisplayName`
- Register page redirects to Login page

## Removed Pages

- Register → redirect to Login
- ForgotPassword, ForgotPasswordConfirmation
- ResetPassword, ResetPasswordConfirmation
- ChangePassword (account management)
- Password fields from Login page

Keep: ExternalLogin, ConfirmEmail (for future email change flows), account management (minus password).

## New Entity: LoginToken

```csharp
public sealed class LoginToken
{
    public int Id { get; set; }
    public string Email { get; set; } = "";       // normalized, used before user exists
    public string? UserId { get; set; }            // FK → ApplicationUser, set after user creation
    public string Token { get; set; } = "";        // GUID as string
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public ApplicationUser? User { get; set; }
}
```

- Unique index on `Token`
- Index on `Email` + `CreatedAtUtc` (for rate limiting queries)

## Email Infrastructure: Azure Communication Services

- New NuGet: `Azure.Communication.Email`
- New service: `AcsTransactionalEmailService`
- Config section: `AzureCommunication`
  - `ConnectionString` — ACS resource connection string
  - `SenderAddress` — e.g., `DoNotReply@{acs-domain}.azurecomm.net`
- Completely separate from Microsoft Graph (which stays for inbox/organizer correspondence)
- Magic link emails never touch the shared mailbox

### Email Content

Subject: `Přihlášení — Ovčina registrace`

Body (plain text):
```
Dobrý den,

Pro přihlášení do registrace Ovčina klikněte na následující odkaz:

{link}

Odkaz je platný 60 minut. Pokud jste o přihlášení nežádali, tento e-mail ignorujte.

Ovčina registrace
```

## Security

| Aspect | Value |
|--------|-------|
| Token expiry | 60 minutes |
| Token reuse | One-time only (`IsUsed` flag) |
| Rate limiting | Max 3 requests per email per 15 minutes |
| Session cookie | 30-day expiry, sliding (refreshes on every request) |
| Session cookie flags | HttpOnly, Secure, SameSite=Lax |

## Session Configuration

```csharp
builder.Services.ConfigureApplicationCookie(options =>
{
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});
```

## DisplayName Population

In `SubmissionService.UpdateContactAsync` or `SubmissionService.AddAttendeeAsync` (on first submission):

```csharp
if (string.IsNullOrWhiteSpace(user.DisplayName))
{
    user.DisplayName = input.PrimaryContactName;
    await userManager.UpdateAsync(user);
}
```

## What Stays Unchanged

- OAuth providers (Microsoft, Google, Seznam) — same as today
- ASP.NET Core Identity — user management, roles, claims
- Authorization policies (AdminOnly, StaffOnly)
- Account management (profile, external logins) — minus password pages
- Microsoft Graph — inbox/organizer email only
