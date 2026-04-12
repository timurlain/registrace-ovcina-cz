# Multi-Email Linking & User Admin Page — Design

**Date:** 2026-04-11
**Status:** Approved

## Problem

People use multiple emails (Gmail, work, Seznam) and get confused which one they used. When OvcinaHra calls registrace's integration API with the "wrong" email, the lookup returns nothing — the person appears unregistered even though they have an account.

## Scope

1. **Alternate emails** (up to 4) linked to a user by organizers in admin
2. **Integration API** resolves any linked email to the right user/person
3. **Login** with alternate email — prompt for confirmation before linking
4. **User admin page** — manage users, alternate emails, roles, quick lookup

## Data Model

New entity `UserEmail`:

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

**EF configuration:**
- Unique index on `NormalizedEmail`
- Index on `UserId`
- Max 4 per user enforced in service layer
- `NormalizedEmail` = `email.Trim().ToUpperInvariant()`

**Validation:**
- Alternate email must not match the user's own primary email
- Must not exist in `AspNetUsers.NormalizedEmail` or `UserEmails.NormalizedEmail`

## Integration API Changes

All lookup endpoints expand to check both `AspNetUsers` and `UserEmails`:

- **`/users/by-email`** — find in AspNetUsers first, fallback to UserEmails → resolve UserId
- **`/registrations/check`** — also resolve via UserEmails → find PersonId → check registrations
- **`/users/{email}/roles`** and **`/users/{email}/has-role`** — same fallback pattern

Shared `ResolveUserByEmail(db, normalizedEmail)` helper used by all endpoints.

## Login Flow

When external provider login finds no linked account:

1. Normal Identity flow runs first (existing external logins)
2. Check `AspNetUsers.NormalizedEmail` (existing behavior)
3. **New:** Check `UserEmails.NormalizedEmail` — if found, show confirmation page:
   - "Nasli jsme ucet **{DisplayName}** propojeny s touto e-mailovou adresou. Je to vas ucet?"
   - **Ano** → `UserManager.AddLoginAsync`, sign in
   - **Ne** → create new account (normal flow)
4. If not found anywhere — normal new account creation

Hook point: `ExternalLogin` callback in `IdentityComponentsEndpointRouteBuilderExtensions.cs`.

## User Admin Page

**Route:** `/admin/users` — Admin role required.

### Quick Lookup (top)
- Text input + search button
- Shows card: display name, primary email, alternate emails, identity roles, game roles per game, registration status per game

### User List (below)
- Table of all users, sortable by name/email/last login
- Search filters by name or email (checks primary + alternates)

### User Detail (click/expand)
- Display name, primary email, last login
- Alternate emails: list with add/remove, max 4, inline validation
- Identity roles: checkboxes for Organizer, Admin
- Game roles per game: list with add/remove

## Non-Goals
- Email verification for alternates (organizer manually adds them, trusted)
- Self-service alternate email management (organizer-only for v1)
- Merging duplicate user accounts
