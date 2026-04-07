# Email-Person Matching on Attendee Save — Design

**Date:** 2026-04-07
**Status:** Approved

## Problem

When a user adds an attendee with an email that already belongs to an existing Person, the unique index on `Person.Email` throws a `DbUpdateException`, crashing the page. Even with the new error handling (v0.6.10), the user only sees a generic "duplicate email" error with no way to resolve it.

## Solution

Before creating a new Person or updating an existing Person's email, check if the email is already assigned to another Person. Handle two cases:

### Case 1: Same name (first + last, case-insensitive)

Silently reuse the existing Person. Link the new Registration to them. Update their birth year to the newly submitted value (soft recollect — birth year data quality is a known issue).

### Case 2: Different name

Throw `EmailConflictException` with the existing person's details. The UI catches it and shows:

> E-mail {email} je přiřazený k osobě {FirstName} {LastName}.
> Chcete přiřadit tohoto účastníka k této osobě, nebo e-mail smazat?

Two buttons:
- **"Použít osobu {Name}"** → reuse existing Person, update birth year
- **"Smazat e-mail"** → save the attendee without email

### Birth year handling

Always take the newly submitted birth year when reusing a person. This is intentional — we want to gradually fix birth year data through re-registration.

## Implementation

### Service layer (`SubmissionService`)

In `AddAttendeeAsync` and `UpdateAttendeeAsync`, before creating/updating Person:

1. If `input.ContactEmail` is not empty, look up `Person` by email (case-insensitive, excluding soft-deleted)
2. If no match → proceed normally (create new Person)
3. If match AND same name → reuse existing Person, update birth year
4. If match AND different name → throw `EmailConflictException(existingPersonId, existingFirstName, existingLastName)`

New flag on `AttendeeInput`: `UseExistingPersonId` (int?) — when set, skip the email check and link to this Person directly. `ClearEmail` (bool) — when set, save without email.

### Exception

```csharp
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

### UI layer (`SubmissionEditor.razor`)

In `SaveAttendeeAsync`, catch `EmailConflictException`:

1. Show an alert with the conflict message
2. Show two buttons: "Použít osobu {Name}" and "Smazat e-mail"
3. "Použít osobu" sets `attendeeInput.UseExistingPersonId = ex.ExistingPersonId` and re-submits
4. "Smazat e-mail" sets `attendeeInput.ClearEmail = true`, clears `attendeeInput.ContactEmail`, and re-submits

### Name matching

Case-insensitive `string.Equals` with `OrdinalIgnoreCase` on both `FirstName` and `LastName`. No fuzzy matching — exact match only. If someone types "Jana" and the existing person is "Jan", it's treated as a different name and the user is asked.
