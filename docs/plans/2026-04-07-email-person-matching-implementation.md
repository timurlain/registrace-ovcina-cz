# Email-Person Matching Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** When saving an attendee with an email that belongs to an existing Person, silently reuse if same name, or ask the user to choose if different name — preventing DbUpdateException crashes.

**Architecture:** Add `EmailConflictException` to the service layer. In `AddAttendeeAsync`/`UpdateAttendeeAsync`, check for email collisions before creating/updating Person. The UI catches the exception, shows a choice dialog, and re-submits with `UseExistingPersonId` or `ClearEmail` flag.

**Tech Stack:** ASP.NET Core, Blazor Server, EF Core, xUnit

**Design doc:** `docs/plans/2026-04-07-email-person-matching-design.md`

---

### Task 1: Add EmailConflictException and email-match logic to SubmissionService

**Files:**
- Create: `src/RegistraceOvcina.Web/Features/Submissions/EmailConflictException.cs`
- Modify: `src/RegistraceOvcina.Web/Features/Submissions/SubmissionService.cs` (AttendeeInput class ~line 939, AddAttendeeAsync ~line 264, UpdateAttendeeAsync ~line 355)
- Test: `tests/RegistraceOvcina.Web.Tests/EmailPersonMatchingTests.cs`

**Step 1: Create EmailConflictException**

Create `src/RegistraceOvcina.Web/Features/Submissions/EmailConflictException.cs`:

```csharp
namespace RegistraceOvcina.Web.Features.Submissions;

public sealed class EmailConflictException(
    int existingPersonId,
    string existingFirstName,
    string existingLastName,
    string email) : Exception($"E-mail {email} je přiřazený k osobě {existingFirstName} {existingLastName}.")
{
    public int ExistingPersonId { get; } = existingPersonId;
    public string ExistingFirstName { get; } = existingFirstName;
    public string ExistingLastName { get; } = existingLastName;
    public string ConflictEmail { get; } = email;
}
```

**Step 2: Add flags to AttendeeInput**

In `SubmissionService.cs`, find the `AttendeeInput` class (around line 939) and add two properties:

```csharp
public int? UseExistingPersonId { get; set; }
public bool ClearEmail { get; set; }
```

**Step 3: Write failing tests**

Create `tests/RegistraceOvcina.Web.Tests/EmailPersonMatchingTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Submissions;

namespace RegistraceOvcina.Web.Tests;

public sealed class EmailPersonMatchingTests
{
    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static SubmissionService CreateService(ApplicationDbContext db)
    {
        return new SubmissionService(
            new TestDbContextFactory(db),
            new SubmissionPricingService(TimeProvider.System),
            TimeProvider.System);
    }

    private static async Task<(ApplicationUser user, RegistrationSubmission submission)> SeedSubmission(ApplicationDbContext db)
    {
        var user = new ApplicationUser
        {
            Id = "user1",
            UserName = "test@test.com",
            Email = "test@test.com",
            NormalizedEmail = "TEST@TEST.COM",
            NormalizedUserName = "TEST@TEST.COM",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(user);

        var game = new Game
        {
            Name = "Test Game",
            IsPublished = true,
            StartsAtUtc = DateTime.UtcNow.AddDays(30),
            EndsAtUtc = DateTime.UtcNow.AddDays(31),
            RegistrationClosesAtUtc = DateTime.UtcNow.AddDays(20),
            PlayerBasePrice = 100m,
            AdultHelperBasePrice = 0m,
            BankAccount = "CZ1234",
            BankAccountName = "Test"
        };
        db.Games.Add(game);
        await db.SaveChangesAsync();

        var submission = new RegistrationSubmission
        {
            GameId = game.Id,
            RegistrantUserId = user.Id,
            PrimaryContactName = "Test User",
            PrimaryEmail = "test@test.com",
            PrimaryPhone = "+420123456789",
            GroupName = "Test Group",
            Status = SubmissionStatus.Draft,
            LastEditedAtUtc = DateTime.UtcNow,
            ExpectedTotalAmount = 0m
        };
        db.RegistrationSubmissions.Add(submission);
        await db.SaveChangesAsync();

        return (user, submission);
    }

    [Fact]
    public async Task AddAttendee_SameName_SameEmail_ReusesExistingPerson()
    {
        using var db = CreateDb();
        var (user, submission) = await SeedSubmission(db);

        // Seed existing person with email
        var existingPerson = new Person
        {
            FirstName = "Adam",
            LastName = "Richtar",
            BirthYear = 2000,
            Email = "adam@example.com",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.People.Add(existingPerson);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var input = new AttendeeInput
        {
            FirstName = "Adam",
            LastName = "Richtar",
            BirthYear = 2001, // different birth year — should be updated
            ContactEmail = "adam@example.com",
            AttendeeType = AttendeeType.Adult,
            AdultRole_Spectator = true
        };

        await service.AddAttendeeAsync(submission.Id, user.Id, input);

        // Should reuse existing person, not create a new one
        var people = await db.People.Where(p => p.Email == "adam@example.com").ToListAsync();
        Assert.Single(people);
        Assert.Equal(existingPerson.Id, people[0].Id);
        Assert.Equal(2001, people[0].BirthYear); // birth year updated
    }

    [Fact]
    public async Task AddAttendee_DifferentName_SameEmail_ThrowsEmailConflict()
    {
        using var db = CreateDb();
        var (user, submission) = await SeedSubmission(db);

        var existingPerson = new Person
        {
            FirstName = "Adam",
            LastName = "Richtar",
            BirthYear = 2000,
            Email = "adam@example.com",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.People.Add(existingPerson);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var input = new AttendeeInput
        {
            FirstName = "Jana",
            LastName = "Nová",
            BirthYear = 1985,
            ContactEmail = "adam@example.com",
            AttendeeType = AttendeeType.Adult,
            AdultRole_Spectator = true
        };

        var ex = await Assert.ThrowsAsync<EmailConflictException>(
            () => service.AddAttendeeAsync(submission.Id, user.Id, input));

        Assert.Equal(existingPerson.Id, ex.ExistingPersonId);
        Assert.Equal("Adam", ex.ExistingFirstName);
        Assert.Equal("Richtar", ex.ExistingLastName);
    }

    [Fact]
    public async Task AddAttendee_DifferentName_UseExistingPersonId_ReusesAndUpdates()
    {
        using var db = CreateDb();
        var (user, submission) = await SeedSubmission(db);

        var existingPerson = new Person
        {
            FirstName = "Adam",
            LastName = "Richtar",
            BirthYear = 2000,
            Email = "adam@example.com",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.People.Add(existingPerson);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var input = new AttendeeInput
        {
            FirstName = "Jana",
            LastName = "Nová",
            BirthYear = 1985,
            ContactEmail = "adam@example.com",
            AttendeeType = AttendeeType.Adult,
            AdultRole_Spectator = true,
            UseExistingPersonId = existingPerson.Id
        };

        await service.AddAttendeeAsync(submission.Id, user.Id, input);

        // Should reuse existing person
        var reg = await db.Registrations.Include(r => r.Person).FirstAsync();
        Assert.Equal(existingPerson.Id, reg.PersonId);
    }

    [Fact]
    public async Task AddAttendee_ClearEmail_SavesWithoutEmail()
    {
        using var db = CreateDb();
        var (user, submission) = await SeedSubmission(db);

        var existingPerson = new Person
        {
            FirstName = "Adam",
            LastName = "Richtar",
            BirthYear = 2000,
            Email = "adam@example.com",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.People.Add(existingPerson);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var input = new AttendeeInput
        {
            FirstName = "Jana",
            LastName = "Nová",
            BirthYear = 1985,
            ContactEmail = "adam@example.com",
            AttendeeType = AttendeeType.Adult,
            AdultRole_Spectator = true,
            ClearEmail = true
        };

        await service.AddAttendeeAsync(submission.Id, user.Id, input);

        // New person created without email
        var newPerson = await db.People.Where(p => p.FirstName == "Jana").FirstAsync();
        Assert.Null(newPerson.Email);
        Assert.Equal(2, await db.People.CountAsync()); // existing + new
    }
}

/// <summary>
/// Wrapper to reuse a single DbContext instance for testing (IDbContextFactory pattern).
/// </summary>
file sealed class TestDbContextFactory(ApplicationDbContext db) : IDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext() => db;
    public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(db);
}
```

**Step 4: Run tests — verify they fail**

```bash
cd "C:/Users/TomášPajonk/source/repos/timurlain/registrace-ovcina-cz"
dotnet test tests/RegistraceOvcina.Web.Tests --filter "EmailPersonMatchingTests" -v minimal
```

Expected: FAIL (EmailConflictException not found, AttendeeInput properties not found).

**Step 5: Implement email-match logic in AddAttendeeAsync**

In `SubmissionService.AddAttendeeAsync` (around line 264), BEFORE the `var person = new Person { ... }` block, add:

```csharp
// Handle ClearEmail flag
if (input.ClearEmail)
{
    input.ContactEmail = null;
}

// Handle UseExistingPersonId flag — reuse the specified person
if (input.UseExistingPersonId.HasValue)
{
    var existingPerson = await db.People.FindAsync([input.UseExistingPersonId.Value], cancellationToken)
        ?? throw new ValidationException("Zadaná osoba neexistuje.");

    existingPerson.BirthYear = input.BirthYear;
    existingPerson.UpdatedAtUtc = nowUtc;

    // Create registration linked to existing person
    var registration = new Registration
    {
        Person = existingPerson,
        SubmissionId = submission.Id,
        AttendeeType = input.AttendeeType,
        PlayerSubType = input.AttendeeType == AttendeeType.Player ? input.PlayerSubType : null,
        AdultRoles = input.AttendeeType == AttendeeType.Adult ? input.ComputedAdultRoles : AdultRoleFlags.None,
        CharacterName = string.IsNullOrWhiteSpace(input.CharacterName) ? null : input.CharacterName.Trim(),
        LodgingPreference = input.LodgingPreference,
        RegistrantNote = string.IsNullOrWhiteSpace(input.AttendeeNote) ? null : input.AttendeeNote.Trim(),
        ContactEmail = string.IsNullOrWhiteSpace(input.ContactEmail) ? null : input.ContactEmail.Trim(),
        ContactPhone = string.IsNullOrWhiteSpace(input.ContactPhone) ? null : input.ContactPhone.Trim(),
        GuardianName = string.IsNullOrWhiteSpace(input.GuardianName) ? null : input.GuardianName.Trim(),
        GuardianRelationship = string.IsNullOrWhiteSpace(input.GuardianRelationship) ? null : input.GuardianRelationship.Trim(),
        GuardianAuthorizationConfirmed = input.GuardianAuthorizationConfirmed,
        CreatedAtUtc = nowUtc,
        UpdatedAtUtc = nowUtc
    };

    db.Registrations.Add(registration);
    submission.LastEditedAtUtc = nowUtc;
    await db.SaveChangesAsync(cancellationToken);
    SaveFoodOrdersFromInput(db, registration, input, submission.Game);
    RecalculateIfSubmitted(submission);
    await db.SaveChangesAsync(cancellationToken);

    db.AuditLogs.Add(new AuditLog
    {
        EntityType = nameof(Registration),
        EntityId = registration.Id.ToString(),
        Action = "AttendeeAdded",
        ActorUserId = userId,
        CreatedAtUtc = nowUtc,
    });
    await db.SaveChangesAsync(cancellationToken);
    return;
}

// Check for email collision with existing person
if (!string.IsNullOrWhiteSpace(input.ContactEmail))
{
    var normalizedEmail = input.ContactEmail.Trim().ToLowerInvariant();
    var existingPerson = await db.People
        .FirstOrDefaultAsync(p => !p.IsDeleted && p.Email != null
            && p.Email.ToLower() == normalizedEmail, cancellationToken);

    if (existingPerson is not null)
    {
        var nameMatch = string.Equals(existingPerson.FirstName, input.FirstName.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(existingPerson.LastName, input.LastName.Trim(), StringComparison.OrdinalIgnoreCase);

        if (nameMatch)
        {
            // Same name — silently reuse, update birth year
            input.UseExistingPersonId = existingPerson.Id;
            existingPerson.BirthYear = input.BirthYear;
            existingPerson.UpdatedAtUtc = nowUtc;
            // Recurse with the flag set
            await AddAttendeeAsync(submissionId, userId, input, cancellationToken);
            return;
        }
        else
        {
            throw new EmailConflictException(
                existingPerson.Id,
                existingPerson.FirstName,
                existingPerson.LastName,
                input.ContactEmail.Trim());
        }
    }
}
```

NOTE: Be careful with the recursion approach — an alternative is to just set `UseExistingPersonId` and fall through to the existing code path with a goto or restructure. The implementer should choose the cleanest approach that avoids code duplication. The key is: if `UseExistingPersonId` is set, use that person instead of creating a new one.

**Step 6: Add similar logic to UpdateAttendeeAsync**

In `UpdateAttendeeAsync` (around line 355), before `registration.Person.Email = ...`, add a check:

```csharp
// Check email collision when email changes
var newEmail = string.IsNullOrWhiteSpace(input.ContactEmail) ? null : input.ContactEmail.Trim();
if (input.ClearEmail) newEmail = null;

if (newEmail is not null && !string.Equals(registration.Person.Email, newEmail, StringComparison.OrdinalIgnoreCase))
{
    var normalizedEmail = newEmail.ToLowerInvariant();
    var existingPerson = await db.People
        .FirstOrDefaultAsync(p => !p.IsDeleted && p.Id != registration.PersonId
            && p.Email != null && p.Email.ToLower() == normalizedEmail, cancellationToken);

    if (existingPerson is not null)
    {
        var nameMatch = string.Equals(existingPerson.FirstName, input.FirstName.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(existingPerson.LastName, input.LastName.Trim(), StringComparison.OrdinalIgnoreCase);

        if (!nameMatch)
        {
            throw new EmailConflictException(
                existingPerson.Id, existingPerson.FirstName, existingPerson.LastName, newEmail);
        }
        // Same name on update — just clear duplicate email on old person and proceed
    }
}
```

**Step 7: Run tests**

```bash
cd "C:/Users/TomášPajonk/source/repos/timurlain/registrace-ovcina-cz"
dotnet test tests/RegistraceOvcina.Web.Tests --filter "EmailPersonMatchingTests" -v minimal
```

Expected: 4 tests PASS.

**Step 8: Build and commit**

```bash
dotnet build --no-restore
git add src/RegistraceOvcina.Web/Features/Submissions/ tests/RegistraceOvcina.Web.Tests/EmailPersonMatchingTests.cs
git commit -m "feat: email-person matching — reuse or conflict on duplicate email

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Handle EmailConflictException in SubmissionEditor UI

**Files:**
- Modify: `src/RegistraceOvcina.Web/Components/Pages/Registrations/SubmissionEditor.razor` (~line 896 SaveAttendeeAsync, ~line 750 fields)

**Step 1: Add conflict state fields**

In the `@code` block, near the existing `errorMessage` field (around line 750), add:

```csharp
private EmailConflictException? emailConflict;
```

Add using at top of file:
```csharp
@using RegistraceOvcina.Web.Features.Submissions
```

(Check if it's already there from `SubmissionService` usage.)

**Step 2: Add conflict UI**

In the template, after the existing `@if (!string.IsNullOrWhiteSpace(errorMessage))` alert div (around line 39-41), add:

```razor
@if (emailConflict is not null)
{
    <div class="alert alert-warning" role="alert">
        <p class="mb-2">
            E-mail <strong>@emailConflict.ConflictEmail</strong> je přiřazený k osobě
            <strong>@emailConflict.ExistingFirstName @emailConflict.ExistingLastName</strong>.
        </p>
        <div class="d-flex gap-2">
            <button class="btn btn-sm btn-primary" @onclick="UseExistingPerson">
                Použít osobu @emailConflict.ExistingFirstName @emailConflict.ExistingLastName
            </button>
            <button class="btn btn-sm btn-outline-secondary" @onclick="ClearEmailAndRetry">
                Smazat e-mail
            </button>
        </div>
    </div>
}
```

**Step 3: Add handler methods**

In the `@code` block, add:

```csharp
private async Task UseExistingPerson()
{
    if (emailConflict is null) return;
    attendeeInput.UseExistingPersonId = emailConflict.ExistingPersonId;
    emailConflict = null;
    await SaveAttendeeAsync();
}

private async Task ClearEmailAndRetry()
{
    attendeeInput.ClearEmail = true;
    attendeeInput.ContactEmail = "";
    emailConflict = null;
    await SaveAttendeeAsync();
}
```

**Step 4: Catch EmailConflictException in SaveAttendeeAsync**

In `SaveAttendeeAsync`, add a catch block BEFORE the existing `catch (ValidationException)`:

```csharp
catch (EmailConflictException ex)
{
    emailConflict = ex;
    errorMessage = null; // clear any previous error — the conflict UI handles this
}
```

Also clear `emailConflict` at the start of `SaveAttendeeAsync`:

```csharp
emailConflict = null;
```

**Step 5: Build**

```bash
cd "C:/Users/TomášPajonk/source/repos/timurlain/registrace-ovcina-cz"
dotnet build src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj --no-restore
```

**Step 6: Commit**

```bash
git add src/RegistraceOvcina.Web/Components/Pages/Registrations/SubmissionEditor.razor
git commit -m "feat: show email conflict dialog with reuse/clear options

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Push and update PR

**Step 1: Run all tests**

```bash
cd "C:/Users/TomášPajonk/source/repos/timurlain/registrace-ovcina-cz"
dotnet test tests/RegistraceOvcina.Web.Tests -v minimal
```

Expected: All tests pass (51 existing + 4 new = 55).

**Step 2: Push**

```bash
git push origin hotfix/v0.6.10
```

The existing PR #83 will be updated automatically.
