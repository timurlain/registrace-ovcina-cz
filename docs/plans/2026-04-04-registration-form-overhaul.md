# Registration Form Overhaul — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Rework the attendee registration form to match the ChangeRequirements1 document — new role system, character name, food, lodging, edit attendees, placeholders, mandatory field markers.

**Architecture:** Vertical slices — each task delivers a working increment. Domain model changes first, then UI, then new sections (food, lodging). All form POST / PRG pattern stays as-is.

**Tech Stack:** .NET 10, Blazor Server (SSR with enhanced nav), EF Core + PostgreSQL, existing app.css theme.

---

## Task 1: Header rename + placeholder text on contact section

**Files:**
- Modify: `src/RegistraceOvcina.Web/Components/Pages/Registrations/SubmissionEditor.razor:47-75`

**Step 1: Update the contact section header**

Change line 49 from:
```razor
<h2 class="h4 mb-3">Kontakt skupiny nebo rodiny</h2>
```
to:
```razor
<h2 class="h4 mb-3">Přihlašovatel / registrátor — hlavní kontakt skupiny nebo rodiny</h2>
```

Also update the read-only submitted view (around line 217) heading if it references the same text.

**Step 2: Add placeholder attributes to contact fields**

```html
<!-- line 56: Hlavní kontakt -->
<input ... placeholder="např. Jana Nováková" ... />

<!-- line 60: Kontaktní e-mail -->
<input ... placeholder="např. jana@email.cz" ... />

<!-- line 64: Kontaktní telefon -->
<input ... placeholder="např. 721 123 456" ... />

<!-- line 68: Poznámka -->
<textarea ... placeholder="Cokoliv, co chcete organizátorům sdělit předem."></textarea>
```

**Step 3: Build and verify**

Run: `dotnet build src/RegistraceOvcina.Web`
Expected: Build succeeded.

**Step 4: Commit**

```bash
git add src/RegistraceOvcina.Web/Components/Pages/Registrations/SubmissionEditor.razor
git commit -m "feat: rename contact header and add placeholder hints"
```

---

## Task 2: Rework domain model — role system, character name, lodging, attendee note

The current `RegistrationRole` enum (`Player, Npc, Monster, TechSupport`) is replaced by a two-branch system: **Player** (single sub-type) and **Adult** (multi-select roles).

**Files:**
- Modify: `src/RegistraceOvcina.Web/Data/ApplicationModels.cs`

**Step 1: Add new enums, keep old enum temporarily**

Add after the existing `RegistrationRole` enum:

```csharp
public enum AttendeeType
{
    Player = 0,
    Adult = 1
}

public enum PlayerSubType
{
    /// <summary>hrající samostatné dítě (cca 10+, PVP hráč)</summary>
    Pvp = 0,
    /// <summary>hrající samostatné dítě (cca 8+)</summary>
    Independent = 1,
    /// <summary>hrající dítě ve skupince s hraničárem (cca 5–7)</summary>
    WithRanger = 2,
    /// <summary>hrající dítě v doprovodu rodiče (cca 4+)</summary>
    WithParent = 3
}

[Flags]
public enum AdultRoleFlags
{
    None = 0,
    /// <summary>hrát cizí postavu / příšeru (skřet, kostlivec, vlci…)</summary>
    PlayMonster = 1,
    /// <summary>pomoci organizaci (obchodník, příručí ve městech)</summary>
    OrganizationHelper = 2,
    /// <summary>technická organizace (svačiny, rozvoz jídla, spojka)</summary>
    TechSupport = 4,
    /// <summary>vést skupinku menších dětí (hraničář)</summary>
    RangerLeader = 8,
    /// <summary>pouze přihlížející</summary>
    Spectator = 16
}

public enum LodgingPreference
{
    /// <summary>Chci spát uvnitř (budova)</summary>
    Indoor = 0,
    /// <summary>Mám vlastní stan</summary>
    OwnTent = 1,
    /// <summary>Mohu spát venku / pod širákem</summary>
    CampOutdoor = 2,
    /// <summary>Neplánuji přenocovat</summary>
    NotStaying = 3
}
```

**Step 2: Update the Registration entity**

Replace/augment properties on `Registration`:

```csharp
// Replace the old Role property
public AttendeeType AttendeeType { get; set; }
public PlayerSubType? PlayerSubType { get; set; }
public AdultRoleFlags AdultRoles { get; set; }

// New fields
public string? CharacterName { get; set; }
public LodgingPreference? LodgingPreference { get; set; }
```

Keep `RegistrantNote` — it already exists on `Registration` (line 153) and we will surface it in the UI.

Keep `RegistrationRole Role` property for now (it will be removed after migration). Mark it `[Obsolete]`.

**Step 3: Build and verify**

Run: `dotnet build src/RegistraceOvcina.Web`
Expected: Warnings about obsolete `Role` usage — that is fine for now.

**Step 4: Commit**

```bash
git add src/RegistraceOvcina.Web/Data/ApplicationModels.cs
git commit -m "feat: add new role system, character name, and lodging to domain model"
```

---

## Task 3: EF Core migration + DbContext mapping

**Files:**
- Modify: `src/RegistraceOvcina.Web/Data/ApplicationDbContext.cs`
- Create: new migration file (auto-generated)

**Step 1: Update DbContext OnModelCreating for new columns**

In the `Registration` entity configuration block, add:

```csharp
entity.Property(e => e.AttendeeType).HasDefaultValue(AttendeeType.Player);
entity.Property(e => e.PlayerSubType);
entity.Property(e => e.AdultRoles).HasDefaultValue(AdultRoleFlags.None);
entity.Property(e => e.CharacterName).HasMaxLength(200);
entity.Property(e => e.LodgingPreference);
```

**Step 2: Create migration**

Run: `dotnet ef migrations add AddRoleSystemCharacterNameLodging --project src/RegistraceOvcina.Web`

**Step 3: Add data migration logic**

In the generated migration `Up` method, after adding columns but before removing anything, add SQL to migrate existing data:

```sql
-- Map old Role values to new AttendeeType
UPDATE "Registrations" SET "AttendeeType" = 0 WHERE "Role" = 0; -- Player stays Player
UPDATE "Registrations" SET "AttendeeType" = 1 WHERE "Role" IN (1, 2, 3); -- Npc/Monster/TechSupport → Adult

-- Map old roles to AdultRoleFlags
UPDATE "Registrations" SET "AdultRoles" = 1 WHERE "Role" = 2;  -- Monster → PlayMonster
UPDATE "Registrations" SET "AdultRoles" = 2 WHERE "Role" = 1;  -- Npc → OrganizationHelper
UPDATE "Registrations" SET "AdultRoles" = 4 WHERE "Role" = 3;  -- TechSupport → TechSupport
```

**Step 4: Remove obsolete Role column (optional — can defer)**

If safe, drop the old `Role` column in this migration. Otherwise keep it and remove later.

**Step 5: Apply migration locally**

Run: `dotnet ef database update --project src/RegistraceOvcina.Web`

**Step 6: Build and verify**

Run: `dotnet build src/RegistraceOvcina.Web`
Expected: Build succeeded, no errors.

**Step 7: Commit**

```bash
git add src/RegistraceOvcina.Web/Data/ 
git commit -m "feat: migration for new role system, character name, and lodging"
```

---

## Task 4: Update service layer — AttendeeInput, ViewModel, SubmissionService

**Files:**
- Modify: `src/RegistraceOvcina.Web/Features/Submissions/SubmissionService.cs`

**Step 1: Update AttendeeInput**

Replace the `Role` property and add new fields:

```csharp
public AttendeeType AttendeeType { get; set; } = AttendeeType.Player;

public PlayerSubType? PlayerSubType { get; set; }

public AdultRoleFlags AdultRoles { get; set; }

[StringLength(200)]
public string? CharacterName { get; set; }

public LodgingPreference? LodgingPreference { get; set; }

[StringLength(4000)]
public string? AttendeeNote { get; set; }
```

Update `Validate()` method:
- If `AttendeeType == Player`, require `PlayerSubType` to have a value.
- If `AttendeeType == Adult`, require at least one flag in `AdultRoles`.

Remove or replace the old `Role` property.

**Step 2: Update AttendeeViewModel**

Replace `RegistrationRole Role` with:
```csharp
public AttendeeType AttendeeType,
public PlayerSubType? PlayerSubType,
public AdultRoleFlags AdultRoles,
public string? CharacterName,
public LodgingPreference? LodgingPreference,
public string? AttendeeNote
```

**Step 3: Update AddAttendeeAsync in SubmissionService**

Map new input fields to Registration entity:
```csharp
AttendeeType = input.AttendeeType,
PlayerSubType = input.AttendeeType == AttendeeType.Player ? input.PlayerSubType : null,
AdultRoles = input.AttendeeType == AttendeeType.Adult ? input.AdultRoles : AdultRoleFlags.None,
CharacterName = string.IsNullOrWhiteSpace(input.CharacterName) ? null : input.CharacterName.Trim(),
LodgingPreference = input.LodgingPreference,
RegistrantNote = string.IsNullOrWhiteSpace(input.AttendeeNote) ? null : input.AttendeeNote.Trim(),
```

Kingdom preference: only set for Players (same as current behavior).

**Step 4: Update GetSubmissionAsync projection**

Add new fields to the `AttendeeViewModel` mapping:
```csharp
x.AttendeeType,
x.PlayerSubType,
x.AdultRoles,
x.CharacterName,
x.LodgingPreference,
x.RegistrantNote
```

**Step 5: Update SubmissionPricingService**

The pricing service uses `Role` to determine price. Update to use `AttendeeType`:
- `AttendeeType.Player` → `PlayerBasePrice`
- `AttendeeType.Adult` → `AdultHelperBasePrice`

**Step 6: Add UpdateAttendeeAsync method to SubmissionService**

```csharp
public async Task UpdateAttendeeAsync(
    int submissionId,
    int registrationId,
    string userId,
    AttendeeInput input,
    CancellationToken cancellationToken = default)
```

Logic: load owned submission (draft), find the registration, update Person fields (first name, last name, birth year), update Registration fields (attendee type, sub-type, adult roles, character name, lodging, note, guardian, kingdom preference, contacts). Save + audit log "AttendeeUpdated".

**Step 7: Build and verify**

Run: `dotnet build src/RegistraceOvcina.Web`
Expected: Build succeeded.

**Step 8: Commit**

```bash
git add src/RegistraceOvcina.Web/Features/Submissions/
git commit -m "feat: update service layer for new role system and edit attendee"
```

---

## Task 5: Add edit-attendee POST endpoint

**Files:**
- Modify: `src/RegistraceOvcina.Web/Program.cs`

**Step 1: Add the MapPost endpoint**

After the existing `/prihlasky/{submissionId}/ucastnici` endpoint, add:

```csharp
app.MapPost(
    "/prihlasky/{submissionId:int}/ucastnici/{registrationId:int}/upravit",
    async (int submissionId, int registrationId, [FromForm] AttendeeInput input,
           HttpContext httpContext, SubmissionService submissionService,
           UserManager<ApplicationUser> userManager, IAntiforgery antiforgery) =>
    {
        // Same pattern as add attendee: validate antiforgery, get user, call service, redirect with query param
    })
    .DisableAntiforgery(); // antiforgery validated manually, same pattern as existing endpoints
```

Redirect to: `/prihlasky/{submissionId}?attendeeUpdated=1`

**Step 2: Build and verify**

Run: `dotnet build src/RegistraceOvcina.Web`

**Step 3: Commit**

```bash
git add src/RegistraceOvcina.Web/Program.cs
git commit -m "feat: add edit-attendee POST endpoint"
```

---

## Task 6: Rework attendee form UI — role selection, character name, note, placeholders, asterisks

This is the biggest UI task. The attendee form in `SubmissionEditor.razor` lines 132–209 gets overhauled.

**Files:**
- Modify: `src/RegistraceOvcina.Web/Components/Pages/Registrations/SubmissionEditor.razor`
- Modify: `src/RegistraceOvcina.Web/wwwroot/app.css`

**Step 1: Add required-field asterisk CSS**

In `app.css`, add:

```css
.form-label.required::after {
    content: " *";
    color: var(--color-accent);
}
```

**Step 2: Update attendee form labels with `required` class**

Mark Jméno, Příjmení, Rok narození labels as required:
```html
<label class="form-label required" for="attendee-first-name">Jméno</label>
```

**Step 3: Replace the role `<select>` with a two-branch radio/checkbox system**

Replace the current role `<select>` (lines 149-157) with:

```razor
<div class="col-12">
    <label class="form-label required">Typ účastníka</label>
    <div class="d-flex gap-4 mb-2">
        <div class="form-check">
            <input class="form-check-input" type="radio" name="AttendeeType"
                   id="type-player" value="Player" checked />
            <label class="form-check-label" for="type-player">Hráč (dítě)</label>
        </div>
        <div class="form-check">
            <input class="form-check-input" type="radio" name="AttendeeType"
                   id="type-adult" value="Adult" />
            <label class="form-check-label" for="type-adult">Dospělý</label>
        </div>
    </div>
</div>

<!-- Player sub-type (single select, shown when AttendeeType=Player) -->
<div class="col-12" id="player-subtype-section">
    <label class="form-label required">Kategorie hráče</label>
    <div class="form-check">
        <input class="form-check-input" type="radio" name="PlayerSubType"
               id="pst-pvp" value="Pvp" />
        <label class="form-check-label" for="pst-pvp">
            hrající samostatné dítě (cca 10+, které chce naplno soutěžit s dalšími — PVP hráč)
        </label>
    </div>
    <div class="form-check">
        <input class="form-check-input" type="radio" name="PlayerSubType"
               id="pst-independent" value="Independent" />
        <label class="form-check-label" for="pst-independent">
            hrající samostatné dítě (cca 8+)
        </label>
    </div>
    <div class="form-check">
        <input class="form-check-input" type="radio" name="PlayerSubType"
               id="pst-ranger" value="WithRanger" />
        <label class="form-check-label" for="pst-ranger">
            hrající dítě ve skupince s hraničárem (cca 5–7)
        </label>
    </div>
    <div class="form-check">
        <input class="form-check-input" type="radio" name="PlayerSubType"
               id="pst-parent" value="WithParent" />
        <label class="form-check-label" for="pst-parent">
            hrající dítě v doprovodu rodiče (cca 4+)
        </label>
    </div>
</div>

<!-- Adult roles (multi-select, shown when AttendeeType=Adult) -->
<div class="col-12 d-none" id="adult-roles-section">
    <label class="form-label required">Role dospělého (lze vybrat více)</label>
    <div class="form-check">
        <input class="form-check-input" type="checkbox" name="AdultRoles"
               value="PlayMonster" id="ar-monster" />
        <label class="form-check-label" for="ar-monster">
            se zájmem hrát cizí postavu — příšeru (např. skřet, kostlivec, vlci…)
        </label>
    </div>
    <div class="form-check">
        <input class="form-check-input" type="checkbox" name="AdultRoles"
               value="OrganizationHelper" id="ar-org" />
        <label class="form-check-label" for="ar-org">
            se zájmem pomoci organizaci (ve městech hráčů jako obchodník, či příručí)
        </label>
    </div>
    <div class="form-check">
        <input class="form-check-input" type="checkbox" name="AdultRoles"
               value="TechSupport" id="ar-tech" />
        <label class="form-check-label" for="ar-tech">
            se zájmem pomoci s technickou organizací (chystání svačiny, rozvoz jídla, spojka…)
        </label>
    </div>
    <div class="form-check">
        <input class="form-check-input" type="checkbox" name="AdultRoles"
               value="RangerLeader" id="ar-ranger" />
        <label class="form-check-label" for="ar-ranger">
            se zájmem vést skupinku menších dětí (hraničář)
        </label>
    </div>
    <div class="form-check">
        <input class="form-check-input" type="checkbox" name="AdultRoles"
               value="Spectator" id="ar-spectator" />
        <label class="form-check-label" for="ar-spectator">
            pouze přihlížející
        </label>
    </div>
</div>
```

**Step 4: Add character name field**

After the role section, before kingdom preference:
```html
<div class="col-md-6">
    <label class="form-label" for="attendee-character-name">Jméno postavy</label>
    <input id="attendee-character-name" name="CharacterName"
           class="form-control" placeholder="např. Thorin Pavéza" />
</div>
```

**Step 5: Add attendee note field**

After the contact fields section:
```html
<div class="col-12">
    <label class="form-label" for="attendee-note">
        Poznámka pro organizátory
    </label>
    <div class="form-text mb-1">
        Zejména v jaké skupině a národu děti chtějí nebo naopak nechtějí být.
        Napište i pokud je vám to jedno — velmi nám to usnadní třídění,
        aby byly národy vyrovnané.
    </div>
    <textarea id="attendee-note" name="AttendeeNote" class="form-control" rows="3"
              placeholder="např. Chceme být s kamarády z oddílu ve stejném národě."></textarea>
</div>
```

**Step 6: Add placeholders to attendee input fields**

```html
<!-- Jméno -->       placeholder="např. Jan"
<!-- Příjmení -->    placeholder="např. Novák"
<!-- Rok narození --> placeholder="např. 2015"
<!-- E-mail -->      placeholder="např. jan@email.cz"
<!-- Telefon -->     placeholder="např. 721 123 456"
<!-- Zákonný zástupce --> placeholder="např. Jana Nováková"
<!-- Vztah -->       placeholder="např. matka"
```

**Step 7: Add inline JS for player/adult section toggle**

Since this is SSR with enhanced navigation, use a small `<script>` block or Blazor JS interop to show/hide the player vs adult sub-sections based on the AttendeeType radio selection. Keep it minimal:

```html
<script>
document.addEventListener('change', e => {
    if (e.target.name !== 'AttendeeType') return;
    const isPlayer = e.target.value === 'Player';
    document.getElementById('player-subtype-section')?.classList.toggle('d-none', !isPlayer);
    document.getElementById('adult-roles-section')?.classList.toggle('d-none', isPlayer);
});
</script>
```

**Step 8: Update the @code block**

- Replace `GetRoleLabel` with `GetAttendeeTypeLabel`, `GetPlayerSubTypeLabel`, `GetAdultRolesLabel` methods.
- Update `CreateDefaultAttendeeInput` to use new fields.
- Remove old `RegistrationRole` references.

**Step 9: Update attendee cards display**

In the attendee card (lines 96-128), update the role display line to show the new role info:
```razor
<div class="small text-secondary">
    @GetAttendeeTypeLabel(attendee.AttendeeType)
    @if (attendee.PlayerSubType is { } pst) { <span> · @GetPlayerSubTypeLabel(pst)</span> }
    @if (attendee.AdultRoles != AdultRoleFlags.None) { <span> · @GetAdultRolesLabel(attendee.AdultRoles)</span> }
    · ročník @attendee.BirthYear
</div>
@if (!string.IsNullOrWhiteSpace(attendee.CharacterName))
{
    <div class="small">Postava: <strong>@attendee.CharacterName</strong></div>
}
```

**Step 10: Build and verify**

Run: `dotnet build src/RegistraceOvcina.Web`

**Step 11: Commit**

```bash
git add src/RegistraceOvcina.Web/Components/Pages/Registrations/SubmissionEditor.razor
git add src/RegistraceOvcina.Web/wwwroot/app.css
git commit -m "feat: rework attendee form with new role system, character name, note, placeholders"
```

---

## Task 7: Edit attendee UI

**Files:**
- Modify: `src/RegistraceOvcina.Web/Components/Pages/Registrations/SubmissionEditor.razor`

**Step 1: Add edit state tracking to @code block**

```csharp
private int? editingRegistrationId;

private void StartEditing(AttendeeViewModel attendee)
{
    editingRegistrationId = attendee.RegistrationId;
    attendeeInput = new AttendeeInput
    {
        FirstName = ..., // populate from attendee
        // etc.
    };
}

private void CancelEditing()
{
    editingRegistrationId = null;
    attendeeInput = CreateDefaultAttendeeInput();
}
```

**Step 2: Add "Upravit" button to attendee cards**

Next to the existing "Odebrat" button, add:
```html
<button type="button" class="btn btn-outline-secondary"
        @onclick="() => StartEditing(attendee)">
    Upravit
</button>
```

**Step 3: Conditionally change form action and heading**

When `editingRegistrationId` is set:
- Change form heading from "Přidat účastníka" to "Upravit účastníka"
- Change form `action` to `/prihlasky/{SubmissionId}/ucastnici/{editingRegistrationId}/upravit`
- Change submit button text to "Uložit změny"
- Show a "Zrušit úpravy" cancel button

**Step 4: Handle attendeeUpdated query param**

Add to the `@code` block:
```csharp
[SupplyParameterFromQuery(Name = "attendeeUpdated")]
private int? attendeeUpdated { get; set; }
```

And in `OnParametersSetAsync`:
```csharp
else if (attendeeUpdated == 1) { statusMessage = "Účastník byl upravený."; }
```

**Step 5: Update AttendeeViewModel to carry all fields needed for edit pre-fill**

The ViewModel must carry `FirstName`, `LastName` separately (not just `FullName`). Either:
- Add those to `AttendeeViewModel`, or
- Parse from `FullName` (fragile), or
- Load from service when editing (adds a round trip).

Best: extend `AttendeeViewModel` with the raw fields.

**Step 6: Build and verify**

Run: `dotnet build src/RegistraceOvcina.Web`

**Step 7: Commit**

```bash
git add src/RegistraceOvcina.Web/Components/Pages/Registrations/SubmissionEditor.razor
git add src/RegistraceOvcina.Web/Features/Submissions/SubmissionService.cs
git commit -m "feat: add edit attendee functionality"
```

---

## Task 8: Lodging section in attendee form

**Files:**
- Modify: `src/RegistraceOvcina.Web/Components/Pages/Registrations/SubmissionEditor.razor`

**Step 1: Add lodging radio group after the note field**

```html
<div class="col-12">
    <label class="form-label">Ubytování</label>
    <div class="form-check">
        <input class="form-check-input" type="radio" name="LodgingPreference"
               id="lodge-indoor" value="Indoor" />
        <label class="form-check-label" for="lodge-indoor">Chci spát uvnitř (budova)</label>
    </div>
    <div class="form-check">
        <input class="form-check-input" type="radio" name="LodgingPreference"
               id="lodge-tent" value="OwnTent" />
        <label class="form-check-label" for="lodge-tent">Mám vlastní stan</label>
    </div>
    <div class="form-check">
        <input class="form-check-input" type="radio" name="LodgingPreference"
               id="lodge-outdoor" value="CampOutdoor" />
        <label class="form-check-label" for="lodge-outdoor">Mohu spát venku / pod širákem</label>
    </div>
    <div class="form-check">
        <input class="form-check-input" type="radio" name="LodgingPreference"
               id="lodge-none" value="NotStaying" />
        <label class="form-check-label" for="lodge-none">Neplánuji přenocovat</label>
    </div>
</div>
```

**Step 2: Display lodging in attendee card**

```razor
@if (attendee.LodgingPreference is { } lp)
{
    <div class="small">Ubytování: @GetLodgingLabel(lp)</div>
}
```

Add helper:
```csharp
private static string GetLodgingLabel(LodgingPreference lp) => lp switch
{
    LodgingPreference.Indoor => "Uvnitř",
    LodgingPreference.OwnTent => "Vlastní stan",
    LodgingPreference.CampOutdoor => "Venku / pod širákem",
    LodgingPreference.NotStaying => "Bez přenocování",
    _ => lp.ToString()
};
```

**Step 3: Build and verify**

Run: `dotnet build src/RegistraceOvcina.Web`

**Step 4: Commit**

```bash
git add src/RegistraceOvcina.Web/Components/Pages/Registrations/SubmissionEditor.razor
git commit -m "feat: add lodging preference to attendee form"
```

---

## Task 9: Food registration section (redesign)

This is the most complex new UI section. The existing `FoodOrder` and `MealOption` models support it, but the UI needs to be built from scratch.

**Files:**
- Modify: `src/RegistraceOvcina.Web/Components/Pages/Registrations/SubmissionEditor.razor`
- Modify: `src/RegistraceOvcina.Web/Features/Submissions/SubmissionService.cs`
- Possibly modify: `src/RegistraceOvcina.Web/Program.cs` (new POST endpoint for food)

**Step 1: Extend SubmissionEditorViewModel with meal data**

Add to `SubmissionEditorViewModel`:
```csharp
public IReadOnlyList<MealDayViewModel> MealDays { get; init; } = [];
```

New view model:
```csharp
public sealed record MealDayViewModel(
    DateTime DayUtc,
    string DayLabel,
    IReadOnlyList<MealOptionViewModel> Options);

public sealed record MealOptionViewModel(
    int Id,
    string Name,
    decimal Price);
```

**Step 2: Load meal options in GetSubmissionAsync**

Query `MealOptions` from the game, group by day, project into `MealDayViewModel`.

**Step 3: Add food section UI after attendees**

Show a per-attendee, per-day food selection. Each attendee gets a radio group per meal day:
- dětská porce
- dospělá porce
- zajistím si sám
- budu držet hladovku

Use a separate `<form>` section or integrate into the attendee form as a follow-up step.

**Design decision:** Food ordering is per-attendee and per-day. The simplest approach is a separate food section that appears after at least one attendee is added, with a table/grid: rows = attendees, columns = meal days. Each cell is a radio group.

**Step 4: Add food POST endpoint**

```
POST /prihlasky/{submissionId}/strava
```

Body: list of `{ RegistrationId, MealOptionId, MealDayUtc }` tuples.

**Step 5: Build and verify**

Run: `dotnet build src/RegistraceOvcina.Web`

**Step 6: Commit**

```bash
git add src/RegistraceOvcina.Web/
git commit -m "feat: add food registration section"
```

---

## Task 10: Update submitted (read-only) view

**Files:**
- Modify: `src/RegistraceOvcina.Web/Components/Pages/Registrations/SubmissionEditor.razor`

**Step 1: Update the read-only attendee display (lines 244-258)**

Show the new fields: attendee type, player sub-type, adult roles, character name, lodging, note, food orders.

**Step 2: Build and verify**

Run: `dotnet build src/RegistraceOvcina.Web`

**Step 3: Commit**

```bash
git add src/RegistraceOvcina.Web/Components/Pages/Registrations/SubmissionEditor.razor
git commit -m "feat: update read-only submitted view with new fields"
```

---

## Task 11: Form binding for multi-select checkboxes and new enums

**Important implementation detail.** The form uses plain HTML `<form method="post">` with PRG pattern — NOT Blazor `EditForm`. The POST endpoints in `Program.cs` bind via `[FromForm] AttendeeInput`. We need to ensure:

**Files:**
- Modify: `src/RegistraceOvcina.Web/Features/Submissions/SubmissionService.cs` (AttendeeInput)
- Modify: `src/RegistraceOvcina.Web/Program.cs`

**Step 1: Handle AdultRoles multi-checkbox binding**

HTML checkboxes with the same `name="AdultRoles"` submit multiple values. ASP.NET minimal API `[FromForm]` won't auto-bind a `[Flags]` enum from multiple form values. Two options:

**Option A (recommended):** Add a custom model binder, or
**Option B (simpler):** Accept `AdultRoles` as `string[]` in a wrapper, then combine in the endpoint before calling the service.

Simplest: change `AttendeeInput.AdultRoles` to `List<string>` for form binding, then convert to flags in the service call.

Or even simpler: use individual `bool` properties (`AdultRole_PlayMonster`, `AdultRole_TechSupport`, etc.) and combine them.

**Step 2: Test form submission manually**

Verify the full add-attendee flow works with the new role system.

**Step 3: Build and verify**

Run: `dotnet build src/RegistraceOvcina.Web`

**Step 4: Commit**

```bash
git add src/RegistraceOvcina.Web/
git commit -m "feat: wire form binding for new role system"
```

---

## Task 12: E2E tests

**Files:**
- Modify/Create: `tests/RegistraceOvcina.E2E/` test files

**Step 1: Test adding a player attendee**

Navigate to submission, fill attendee form with player type + sub-type, submit, verify card appears.

**Step 2: Test adding an adult attendee with multiple roles**

Fill form as adult, select multiple checkboxes, submit, verify.

**Step 3: Test editing an attendee**

Add attendee, click edit, change fields, save, verify updated card.

**Step 4: Test validation**

Try submitting without name → expect error. Try player without sub-type → expect error.

**Step 5: Test lodging selection**

Add attendee with lodging, verify it shows in card.

**Step 6: Commit**

```bash
git add tests/RegistraceOvcina.E2E/
git commit -m "test: E2E coverage for attendee form overhaul"
```

---

## Execution Order

Tasks must be done in order 1→12. Some can be parallelized:
- Tasks 1 (header/placeholders) is independent — can run in parallel with Task 2.
- Task 3 depends on Task 2.
- Tasks 4–5 depend on Task 3.
- Tasks 6, 7, 8 depend on Task 4 but are independent of each other — can run in parallel.
- Task 9 depends on Task 4.
- Task 10 depends on all UI tasks (6–9).
- Task 11 depends on Task 6.
- Task 12 depends on all prior tasks.

```
1 ──────────────────────────────────────────────────┐
2 → 3 → 4 → 5 ──┬── 6 (edit) ──┐                  │
                  ├── 7 (food) ──┤                  │
                  ├── 8 (lodge) ─┤                  │
                  └── 11 (bind) ─┤                  │
                                 └── 10 → 12 ───────┘
```
