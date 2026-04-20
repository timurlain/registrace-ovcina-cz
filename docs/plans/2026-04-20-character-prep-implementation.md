# Character Prep Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Let parents pre-select each Player's character name + starting equipment + optional note via a tokenized link, so the LARP starts on time and OvčinaHra import has clean character↔person links.

**Architecture:** Per-game reference table for the 5 equipment options (admin-configurable). Three new columns on `Registration` carry the prep data; three on `RegistrationSubmission` carry the opaque token + send-history timestamps. An anonymous Blazor Server page resolves `/postavy/{token}` to the household's prep view. Staff bulk-send invitations + reminders via the existing Graph outbound pipeline. Organizer dashboard gives per-row visibility and Excel export.

**Tech Stack:** .NET 10, Blazor Server, EF Core (PostgreSQL), ClosedXML (already in csproj for Kingdom export), xUnit, Microsoft Graph for outbound email, Playwright for E2E.

**Design doc:** `docs/plans/2026-04-20-character-prep-design.md`

**Repo root:** `C:\Users\TomášPajonk\source\repos\timurlain\registrace-ovcina-cz`
**Web project cwd for EF commands:** `src/RegistraceOvcina.Web`
**Test project:** `tests/RegistraceOvcina.Tests` (if path differs, adjust — do not move files)

**House rules (carry into every task):**
- No commit without explicit user approval. TDD means: write test → fail → implement → pass → stage. STOP before `git commit`.
- Every property addition or rename to a model class ⇒ EF migration generated in the same task.
- Czech UI strings, Czech date formatting.
- Soft-deleted Persons/Submissions already filtered by global query filter — do not bypass.
- Version bump `0.9.9 → 0.9.10` lives in Task 24 (final task before PR).

**Parallel-friendly clusters** — after Phase 1 (schema) lands, Phases 3-7 can be dispatched to subagents in parallel (each owns a vertical slice). Phase 2 (core services) gates all downstream work. Phase 8 (E2E) comes last.

---

## Phase 1 — Schema & migration (sequential, blocks everything)

### Task 1: Create `StartingEquipmentOption` entity

**Files:**
- Create: `src/RegistraceOvcina.Web/Data/StartingEquipmentOption.cs` (match the folder where existing entities like `Game`, `Registration` live — if they live in a subfolder, place alongside them)
- Modify: `src/RegistraceOvcina.Web/Data/ApplicationDbContext.cs`
- Test: `tests/RegistraceOvcina.Tests/Data/StartingEquipmentOptionConfigurationTests.cs`

**Step 1: Failing test** — assert that `DbContext.StartingEquipmentOptions` is registered, has the six expected columns, a unique `(GameId, Key)` index, and `OnDelete(Cascade)` from Game.

```csharp
public class StartingEquipmentOptionConfigurationTests : DbContextTestBase
{
    [Fact]
    public void DbSet_is_registered_and_entity_has_expected_shape()
    {
        var entity = Context.Model.FindEntityType(typeof(StartingEquipmentOption));
        Assert.NotNull(entity);
        Assert.NotNull(entity!.FindProperty(nameof(StartingEquipmentOption.Key)));
        Assert.Equal(50, entity.FindProperty(nameof(StartingEquipmentOption.Key))!.GetMaxLength());
        Assert.Equal(100, entity.FindProperty(nameof(StartingEquipmentOption.DisplayName))!.GetMaxLength());
        Assert.Equal(500, entity.FindProperty(nameof(StartingEquipmentOption.Description))!.GetMaxLength());
        var uniqueIdx = entity.GetIndexes().Single(i => i.IsUnique);
        Assert.Equal(new[] { nameof(StartingEquipmentOption.GameId), nameof(StartingEquipmentOption.Key) },
            uniqueIdx.Properties.Select(p => p.Name).ToArray());
    }
}
```

**Step 2:** Run `dotnet test --filter StartingEquipmentOptionConfigurationTests` → expect compile error (type doesn't exist).

**Step 3:** Create the entity:

```csharp
namespace RegistraceOvcina.Web.Data;

public sealed class StartingEquipmentOption
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public Game Game { get; set; } = default!;
    public string Key { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
}
```

Add to `ApplicationDbContext`:

```csharp
public DbSet<StartingEquipmentOption> StartingEquipmentOptions => Set<StartingEquipmentOption>();
```

In `OnModelCreating`:

```csharp
builder.Entity<StartingEquipmentOption>(entity =>
{
    entity.HasKey(x => x.Id);
    entity.Property(x => x.Key).HasMaxLength(50).IsRequired();
    entity.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
    entity.Property(x => x.Description).HasMaxLength(500);
    entity.HasIndex(x => new { x.GameId, x.Key }).IsUnique();
    entity.HasOne(x => x.Game)
        .WithMany()
        .HasForeignKey(x => x.GameId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

**Step 4:** Run tests → PASS.

**Step 5:** Stage changes. Do not commit — this task lands together with Tasks 2-4.

---

### Task 2: Add three columns to `Registration`

**Files:**
- Modify: model class `Registration` (search: `class Registration` under `src/RegistraceOvcina.Web/Data`)
- Modify: `src/RegistraceOvcina.Web/Data/ApplicationDbContext.cs` (the `builder.Entity<Registration>` block)
- Test: `tests/RegistraceOvcina.Tests/Data/RegistrationCharacterPrepColumnsTests.cs`

**Step 1:** Failing test:

```csharp
[Fact]
public void Registration_has_character_prep_columns()
{
    var entity = Context.Model.FindEntityType(typeof(Registration));
    Assert.NotNull(entity!.FindProperty("StartingEquipmentOptionId"));
    Assert.True(entity.FindProperty("StartingEquipmentOptionId")!.IsNullable);
    Assert.Equal(4000, entity.FindProperty("CharacterPrepNote")!.GetMaxLength());
    Assert.NotNull(entity.FindProperty("CharacterPrepUpdatedAtUtc"));
}
```

**Step 2:** Run → FAIL (properties don't exist).

**Step 3:** Add to `Registration`:

```csharp
public Guid? StartingEquipmentOptionId { get; set; }
public StartingEquipmentOption? StartingEquipmentOption { get; set; }
public string? CharacterPrepNote { get; set; }
public DateTimeOffset? CharacterPrepUpdatedAtUtc { get; set; }
```

In `ApplicationDbContext.OnModelCreating` inside the `Registration` block, after the existing property configs:

```csharp
entity.Property(x => x.CharacterPrepNote).HasMaxLength(4000);
entity.HasOne(x => x.StartingEquipmentOption)
    .WithMany()
    .HasForeignKey(x => x.StartingEquipmentOptionId)
    .OnDelete(DeleteBehavior.Restrict);
```

**Step 4:** Run tests → PASS.

**Step 5:** Stage; no commit yet.

---

### Task 3: Add three columns to `RegistrationSubmission`

**Files:**
- Modify: `RegistrationSubmission` model class
- Modify: `ApplicationDbContext.cs` (`builder.Entity<RegistrationSubmission>` block)
- Test: `tests/RegistraceOvcina.Tests/Data/RegistrationSubmissionTokenColumnsTests.cs`

**Step 1:** Failing test asserts the three new columns exist, the unique filtered index on `CharacterPrepToken` is configured.

```csharp
[Fact]
public void Submission_has_prep_token_columns_and_unique_filtered_index()
{
    var entity = Context.Model.FindEntityType(typeof(RegistrationSubmission))!;
    Assert.Equal(64, entity.FindProperty("CharacterPrepToken")!.GetMaxLength());
    Assert.NotNull(entity.FindProperty("CharacterPrepInvitedAtUtc"));
    Assert.NotNull(entity.FindProperty("CharacterPrepReminderLastSentAtUtc"));
    var uniqueOnToken = entity.GetIndexes().Single(i =>
        i.Properties.Count == 1 && i.Properties[0].Name == "CharacterPrepToken");
    Assert.True(uniqueOnToken.IsUnique);
    Assert.NotNull(uniqueOnToken.GetFilter()); // partial index filter
}
```

**Step 2:** Run → FAIL.

**Step 3:** Add to `RegistrationSubmission`:

```csharp
public string? CharacterPrepToken { get; set; }
public DateTimeOffset? CharacterPrepInvitedAtUtc { get; set; }
public DateTimeOffset? CharacterPrepReminderLastSentAtUtc { get; set; }
```

In the DbContext entity block:

```csharp
entity.Property(x => x.CharacterPrepToken).HasMaxLength(64);
entity.HasIndex(x => x.CharacterPrepToken)
    .IsUnique()
    .HasFilter("\"CharacterPrepToken\" IS NOT NULL");
```

**Step 4:** Run tests → PASS.

**Step 5:** Stage.

---

### Task 4: Generate and apply the migration

**Files:**
- Create: `src/RegistraceOvcina.Web/Migrations/<timestamp>_AddCharacterPrepAndStartingEquipment.cs`
- Create: snapshot updates

**Step 1:** Generate:

```bash
cd src/RegistraceOvcina.Web && dotnet ef migrations add AddCharacterPrepAndStartingEquipment
```

**Step 2:** Verify the generated `.cs` file contains:
- `CreateTable("StartingEquipmentOptions", ...)` with the 6 columns + unique index on `(GameId, Key)` + FK to `Games` with cascade delete
- `AddColumn` × 3 on `Registrations` (`StartingEquipmentOptionId`, `CharacterPrepNote`, `CharacterPrepUpdatedAtUtc`) + FK + index on `StartingEquipmentOptionId`
- `AddColumn` × 3 on `RegistrationSubmissions` (`CharacterPrepToken`, `CharacterPrepInvitedAtUtc`, `CharacterPrepReminderLastSentAtUtc`) + unique filtered index on `CharacterPrepToken`

**Step 3:** Confirm no stray changes:

```bash
cd src/RegistraceOvcina.Web && dotnet ef migrations has-pending-model-changes
```

Expected output: `No pending model changes are present.`

**Step 4:** Apply against local Postgres:

```bash
cd src/RegistraceOvcina.Web && dotnet ef database update
```

**Step 5:** Run `dotnet test` at repo root. All 77 existing tests + the 3 new config tests = 80 PASS. Stage migration files.

**Phase 1 commit point:** ask user for permission to commit "feat: schema for character prep feature" with migration + entity changes.

---

## Phase 2 — Core services (sequential)

### Task 5: `CharacterPrepTokenService`

**Files:**
- Create: `src/RegistraceOvcina.Web/Features/CharacterPrep/CharacterPrepTokenService.cs`
- Test: `tests/RegistraceOvcina.Tests/Features/CharacterPrep/CharacterPrepTokenServiceTests.cs`

**Behavior:**
- `Task<string> EnsureTokenAsync(Guid submissionId, CancellationToken ct)` — if submission has no token, generate one + save; return current token.
- `Task<string> RotateTokenAsync(Guid submissionId, CancellationToken ct)` — always generate a fresh one, overwrite, save.
- `Task<RegistrationSubmission?> FindBySubmissionTokenAsync(string token, CancellationToken ct)` — single-column indexed lookup.

**Tests (red → green per test):**
1. `EnsureTokenAsync_generates_token_on_first_call` — returns non-empty, 43-ish chars, Base64Url charset (`^[A-Za-z0-9_-]+$`)
2. `EnsureTokenAsync_is_idempotent` — second call returns the same value
3. `RotateTokenAsync_replaces_existing_token` — returns different value, old token no longer resolves
4. `FindBySubmissionTokenAsync_returns_null_for_unknown`
5. `FindBySubmissionTokenAsync_returns_null_for_soft_deleted_submission` — respects the global query filter

**Implementation skeleton:**

```csharp
public sealed class CharacterPrepTokenService(ApplicationDbContext db)
{
    public async Task<string> EnsureTokenAsync(Guid submissionId, CancellationToken ct)
    {
        var submission = await db.RegistrationSubmissions.FirstAsync(x => x.Id == submissionId, ct);
        if (!string.IsNullOrEmpty(submission.CharacterPrepToken))
            return submission.CharacterPrepToken;
        submission.CharacterPrepToken = GenerateToken();
        await db.SaveChangesAsync(ct);
        return submission.CharacterPrepToken;
    }

    public async Task<string> RotateTokenAsync(Guid submissionId, CancellationToken ct) { /* ... */ }
    public Task<RegistrationSubmission?> FindBySubmissionTokenAsync(string token, CancellationToken ct) =>
        db.RegistrationSubmissions.FirstOrDefaultAsync(x => x.CharacterPrepToken == token, ct);

    private static string GenerateToken() =>
        Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(32));
}
```

**DI:** register as scoped in `Program.cs` alongside existing feature services.

---

### Task 6: `CharacterPrepService.GetPrepViewAsync`

**Files:**
- Create: `src/RegistraceOvcina.Web/Features/CharacterPrep/CharacterPrepService.cs`
- Create: `src/RegistraceOvcina.Web/Features/CharacterPrep/CharacterPrepViewModels.cs` (records: `CharacterPrepView`, `CharacterPrepRow`)
- Test: `tests/RegistraceOvcina.Tests/Features/CharacterPrep/CharacterPrepServiceGetViewTests.cs`

**Behavior:**

```csharp
public sealed record CharacterPrepView(
    Guid SubmissionId,
    Guid GameId,
    string GameName,
    bool IsReadOnly,              // Game.StartDateUtc <= now
    IReadOnlyList<CharacterPrepRow> Rows,
    IReadOnlyList<StartingEquipmentOptionView> Options);

public sealed record CharacterPrepRow(
    Guid RegistrationId,
    string PersonFullName,
    string? CharacterName,
    Guid? StartingEquipmentOptionId,
    string? CharacterPrepNote,
    DateTimeOffset? UpdatedAtUtc);

public sealed record StartingEquipmentOptionView(
    Guid Id, string DisplayName, string? Description, int SortOrder);
```

`GetPrepViewAsync(RegistrationSubmission, nowUtc)` filters `AttendeeType == Player`, orders rows by Person lastname+firstname, orders options by `SortOrder`, computes `IsReadOnly` from `Game.StartDateUtc`.

**Tests:**
1. `Returns_only_player_attendees_in_order`
2. `IsReadOnly_true_when_game_started`
3. `Options_ordered_by_SortOrder`
4. `Rows_preserve_existing_values` — round-trip a pre-populated Registration

---

### Task 7: `CharacterPrepService.SaveAsync`

Same file, same test file.

**Behavior:** `Task SaveAsync(Guid submissionId, IEnumerable<CharacterPrepSaveRow> rows, DateTimeOffset nowUtc, CancellationToken ct)`.

```csharp
public sealed record CharacterPrepSaveRow(
    Guid RegistrationId,
    string? CharacterName,
    Guid? StartingEquipmentOptionId,
    string? CharacterPrepNote);
```

Behavior:
- Load registrations for the submission in one query, filter by Player
- For each incoming row, find matching Registration by Id AND SubmissionId (defense: reject mismatched)
- Trim `CharacterName` and `CharacterPrepNote` (convert empty to null)
- Validate `StartingEquipmentOptionId` belongs to the same GameId (reject with `ArgumentException` otherwise)
- Stamp `CharacterPrepUpdatedAtUtc = nowUtc` only when at least one field changed
- `SaveChangesAsync` in a single batch

**Tests:**
1. `Saves_character_name_equipment_note`
2. `Stamps_UpdatedAtUtc_only_when_something_changed`
3. `Trims_and_nulls_empty_strings`
4. `Ignores_rows_for_foreign_submissions`
5. `Ignores_rows_for_non_Player_attendees`
6. `Rejects_equipment_option_from_different_game`
7. `Clearing_all_fields_is_allowed`

---

### Task 8: Bulk-invite filters + reminder throttle (service methods)

**Files:** same `CharacterPrepService.cs`
**Test:** `tests/RegistraceOvcina.Tests/Features/CharacterPrep/CharacterPrepServiceBulkFiltersTests.cs`

**Methods:**
- `Task<IReadOnlyList<RegistrationSubmission>> ListInvitationTargetsAsync(Guid gameId, CancellationToken ct)` — submissions with ≥1 Player attendee AND `CharacterPrepInvitedAtUtc IS NULL`
- `Task<IReadOnlyList<RegistrationSubmission>> ListReminderTargetsAsync(Guid gameId, DateTimeOffset nowUtc, CancellationToken ct)` — invited AND ≥1 Player row with `StartingEquipmentOptionId IS NULL` AND (`CharacterPrepReminderLastSentAtUtc IS NULL` OR `CharacterPrepReminderLastSentAtUtc < nowUtc - 24h`)
- `Task MarkInvitedAsync(Guid submissionId, DateTimeOffset nowUtc, CancellationToken ct)`
- `Task MarkReminderSentAsync(Guid submissionId, DateTimeOffset nowUtc, CancellationToken ct)`

**Tests cover each filter edge including the 24h throttle boundary.**

---

## Phase 3 — Email (depends on Phase 2)

### Task 9: Email template renderer — Pozvánka + Připomínka

**Files:**
- Create: `src/RegistraceOvcina.Web/Features/CharacterPrep/CharacterPrepEmailRenderer.cs`
- Create: Razor components or inline string builders matching the project's existing email template style (check `MailboxSyncService.cs` for reference)
- Test: `tests/RegistraceOvcina.Tests/Features/CharacterPrep/CharacterPrepEmailRendererTests.cs`

**Render contract:**

```csharp
public sealed record RenderedEmail(string Subject, string HtmlBody, string PlainTextBody);

public interface ICharacterPrepEmailRenderer
{
    RenderedEmail RenderPozvanka(CharacterPrepEmailModel model);
    RenderedEmail RenderPripominka(CharacterPrepEmailModel model);
}

public sealed record CharacterPrepEmailModel(
    string GameName,
    DateTimeOffset GameStartDateUtc,
    string PrepUrl,
    IReadOnlyList<string> PlayerFullNames,
    IReadOnlyList<StartingEquipmentOptionView> Options,
    string OrganizerContactEmail);
```

**Tests:** snapshot each template (Verify or hand-rolled golden file). Assert `PrepUrl` appears, subject is correct, player names render, deadline (`GameStart - 3 days`) appears in Pozvánka only.

---

### Task 10: `CharacterPrepMailService` — single-submission sends

**Files:**
- Create: `src/RegistraceOvcina.Web/Features/CharacterPrep/CharacterPrepMailService.cs`
- Test: `tests/RegistraceOvcina.Tests/Features/CharacterPrep/CharacterPrepMailServiceTests.cs`

**Methods:**
- `Task SendPozvankaAsync(Guid submissionId, DateTimeOffset nowUtc, CancellationToken ct)` — ensures token, renders, sends via existing Graph outbound (`MailboxSyncService` or whatever the single-email send path is), writes `EmailMessage` with `LinkedSubmissionId`, marks invited.
- `Task SendPripominkaAsync(Guid submissionId, DateTimeOffset nowUtc, CancellationToken ct)` — requires token already exists, throws if still null. Enforces 24h throttle via service check; send → `EmailMessage` + `MarkReminderSentAsync`.
- `Task SendBulkPozvankaAsync(Guid gameId, DateTimeOffset nowUtc, CancellationToken ct)` — loops `ListInvitationTargetsAsync`, sends each, collects errors, returns `BulkSendResult { Sent, Failed, FirstError }`.
- `Task SendBulkPripominkaAsync(Guid gameId, DateTimeOffset nowUtc, CancellationToken ct)` — same shape.

**Tests:**
1. Pozvánka sends and marks invited
2. Second Pozvánka on same submission is rejected (or: allowed idempotently; pick one — recommendation: allowed, "re-send pozvánka" is a valid organizer action, but behavior must be deterministic — pick NO-OP with a warning in the return result)
3. Připomínka within 24h throws
4. Připomínka after 24h succeeds
5. Bulk pozvánka iterates all targets and emits one EmailMessage row per send
6. Bulk pripominka skips submissions that are fully filled
7. Send failure for one submission does not abort the whole batch — collected into `Failed` count

**DI:** scoped. Register in `Program.cs`.

---

## Phase 4 — Parent-facing prep page (depends on Phase 2)

### Task 11: Route `/postavy/{token}` — anonymous, 404 path

**Files:**
- Create: `src/RegistraceOvcina.Web/Components/Pages/CharacterPrep/CharacterPrep.razor`
- Create: `src/RegistraceOvcina.Web/Components/Pages/CharacterPrep/CharacterPrepNotFound.razor`
- Test: `tests/RegistraceOvcina.Tests/Integration/CharacterPrepPageTests.cs` (WebApplicationFactory)

**Behavior:**
- `@page "/postavy/{Token}"`
- `@attribute [AllowAnonymous]`
- `@rendermode InteractiveServer` (match existing Blazor patterns — check project conventions)
- `OnInitializedAsync` calls `CharacterPrepTokenService.FindBySubmissionTokenAsync`. If null, navigate to `CharacterPrepNotFound` or render the not-found component inline. Copy: *"Odkaz již neplatí nebo je neznámý, kontaktujte organizátory."*
- Minimal chrome layout — no organizer sidebar

**Tests:**
1. `GET /postavy/unknown → 404 page HTML`
2. `GET /postavy/{valid} → 200 and contains game name`
3. Anonymous access works (no cookie required)

---

### Task 12: Prep page card UI + pre-fill

**Same file**: `CharacterPrep.razor`.

**UI:**
- Header + explanatory paragraph
- One card per `CharacterPrepRow`:
  - Title: Person full name
  - Input `Jméno postavy` (`<InputText>` bound to `row.CharacterName`)
  - Radio group `Startovní výbava` bound to `row.StartingEquipmentOptionId`, one radio per option, label = `DisplayName`, subtext = `Description`
  - Textarea `Poznámka` bound to `row.CharacterPrepNote`
  - Footer muted text: *"Uloženo: {UpdatedAtUtc:d. M. yyyy HH:mm}"* if non-null
- Pre-fill from the view model already loaded

**Test (bUnit component test):** `tests/RegistraceOvcina.Tests/Components/CharacterPrepPageRenderTests.cs` — renders cards for 3 Player attendees, radio groups have 5 options, pre-fill values appear in inputs.

---

### Task 13: Prep page save handler

**Same file.**

**Behavior:**
- `Uložit` submits `List<CharacterPrepSaveRow>` derived from bound state
- Call `CharacterPrepService.SaveAsync(submissionId, rows, DateTimeOffset.UtcNow, ct)`
- On success: toast *"Uloženo, díky"*, refresh timestamps from a reload of the view
- On failure: toast error, log via ILogger

**Test (integration):** `tests/RegistraceOvcina.Tests/Integration/CharacterPrepSaveFlowTests.cs` — POST save flow writes DB columns, second GET returns pre-filled values.

---

### Task 14: Read-only mode after game start

**Same file.**

**Behavior:**
- When `view.IsReadOnly`, all `<InputText>`/radio/textarea are `disabled`, save button hidden, banner *"Hra již začala, změny nelze provést."*
- Staff override: detect `User.Identity?.IsAuthenticated && User.IsInRole("Staff")` (check project's actual staff-check convention). If staff, bypass.

**Test:** `Game_started_renders_readonly_unless_staff` — two scenarios, parametrized test.

---

## Phase 5 — Organizer dashboard (depends on Phase 2)

### Task 15: Dashboard route + stats strip

**Files:**
- Create: `src/RegistraceOvcina.Web/Components/Pages/Organizer/CharacterPrepDashboard.razor`
- Service method: `CharacterPrepService.GetDashboardStatsAsync(Guid gameId)` returning record `{ TotalHouseholds, Invited, FullyFilled, Pending }`
- Test: `tests/RegistraceOvcina.Tests/Features/CharacterPrep/CharacterPrepDashboardStatsTests.cs`

**UI:** route `/organizace/hry/{gameId}/priprava-postav`, `[Authorize]` staff policy, 4 stat tiles matching `GameStatsPage` style.

---

### Task 16: Dashboard table + filter + sort

**Same file.**

**Service:** `Task<PagedResult<CharacterPrepDashboardRow>> GetDashboardRowsAsync(Guid gameId, DashboardFilter filter, int page, int pageSize)` with status filter (`All | NotInvited | Waiting | Done`) and search string.

**Row projection:** Household name, Person name, Character name, Option DisplayName, Note (truncated), Status badge, UpdatedAt relative, Action buttons column.

**Test:** `CharacterPrepDashboardRowsTests` — full-text search hits person + character name; status filter matches expected rows.

---

### Task 17: Dashboard action bar (bulk buttons, per-row reminder)

**Same file.**

**Buttons:**
- `Poslat pozvánku (N)` — disabled if N=0 or game started — invokes `SendBulkPozvankaAsync`
- `Poslat připomínku (M)` — disabled if M=0 or game started — invokes `SendBulkPripominkaAsync`
- Per-row `Poslat připomínku` on each `Waiting`/`NotInvited` row
- `Stáhnout Excel` wired to endpoint from Task 22

On success: toast with counts (*"Posláno: 32 pozvánek, 0 chyb"*).

**Test:** `CharacterPrepDashboardActionsTests` — bulk button click sends, Email outbox rows are written. Integration test via WAF.

---

### Task 18: Stats widget on `GameStatsPage`

**Files:**
- Modify: `src/RegistraceOvcina.Web/Components/Pages/Organizer/GameStatsPage.razor`
- Modify: `Features/Stats/GameStatsService.cs` to also return `CharacterPrepFilled` and `CharacterPrepTotal`
- Test: update existing `GameStatsServiceTests` (or add new `GameStatsCharacterPrepTests.cs`)

**UI:** one tile *"Příprava postav: {filled}/{total} vyplněno"* linking to the dashboard.

---

## Phase 6 — Admin config page (depends on Phase 1 only)

### Task 19: `StartingEquipmentOption` CRUD page

**Files:**
- Create: `src/RegistraceOvcina.Web/Components/Pages/Organizer/StartingEquipmentOptions.razor`
- Service: `CharacterPrepOptionsService` (new file `Features/CharacterPrep/CharacterPrepOptionsService.cs`) with `List / Create / Update / Delete / CopyFromGame`
- Test: `tests/RegistraceOvcina.Tests/Features/CharacterPrep/CharacterPrepOptionsServiceTests.cs`

**UI:** route `/organizace/hry/{gameId}/priprava-postav/vybava`, staff-only. Table of current options with inline edit, "Přidat" button, delete guard (fail with message if any Registration references the option).

**Tests:** create, update, soft-fail delete when referenced, copy 5 rows from another game.

---

## Phase 7 — Excel export (depends on Phase 1 + the dashboard service)

### Task 20: `CharacterPrepExportService`

**Files:**
- Create: `src/RegistraceOvcina.Web/Features/CharacterPrep/CharacterPrepExportService.cs`
- Create endpoint: add to existing `IntegrationApiEndpoints.cs` OR create `Features/CharacterPrep/CharacterPrepEndpoints.cs` (follow Kingdom export's pattern, which lives at — grep for its MapGet to confirm)
- Test: `tests/RegistraceOvcina.Tests/Features/CharacterPrep/CharacterPrepExportServiceTests.cs`

**Behavior:** builds XLSX using ClosedXML (same library as `KingdomExportService`). One sheet "Příprava postav" with columns: Hráč, Rok narození, Jméno postavy, Startovní výbava, Poznámka, Domácnost, Email domácnosti. Header bold, frozen row 1, autofilter, auto-width capped at 40. Filename `priprava-postav-{slug}-{yyyyMMdd}.xlsx`.

**Tests:**
1. Non-empty byte array
2. Valid XLSX (roundtrip open)
3. Correct header row
4. Row count == Player attendees count
5. Empty cells stay empty
6. Czech characters encode correctly (compare a `Šárka` cell byte-for-byte after load)

**Endpoint:** `GET /organizace/hry/{gameId}/priprava-postav/export.xlsx`, staff auth, returns `FileContentResult`.

---

## Phase 8 — Submission detail integration (depends on Phase 2-3)

### Task 21: "Příprava postav" section on `SubmissionDetail.razor`

**Files:**
- Modify: `src/RegistraceOvcina.Web/Components/Pages/Organizer/SubmissionDetail.razor`

**UI:** new card between existing sections:
- Current state badge (Nezváno / Čeká / Hotovo)
- Token URL with copy-to-clipboard button (hidden if token null — button "Vygenerovat odkaz" generates via `EnsureTokenAsync`)
- Timeline: *Pozvaná: {date} • Připomenuta: {date}* (hide empty ones)
- Buttons `Poslat pozvánku`, `Poslat připomínku`, `Vygenerovat nový odkaz` (the last one prompts a JS `confirm` since it invalidates the old link)

**Test:** extend `SubmissionDetailTests` or create `SubmissionDetailCharacterPrepSectionTests.cs`. Hit the three buttons, verify DB side effects.

---

## Phase 9 — E2E + final polish

### Task 22: E2E smoke test (Playwright)

**Files:**
- Create: `tests/RegistraceOvcina.E2E/CharacterPrepSmokeTests.cs`

**Scenario:**
1. Seed a game, one submission with two Player attendees, 5 equipment options
2. Organizer logs in, visits dashboard, clicks `Poslat pozvánku` bulk button
3. Extract `CharacterPrepToken` from DB (simulates parent receiving email)
4. Browser opens `/postavy/{token}` anonymously
5. Fill character name + pick equipment for both kids, save
6. Reload page, verify values pre-filled
7. Organizer dashboard shows both rows as `Hotovo`

**Playwright install guard:** first run does `playwright install chromium` if missing (existing harness pattern). Skip with `[Fact(Skip = "...")]` if CI browsers aren't installed.

---

### Task 23: Seed the 5 equipment options for Ovčina 2026

**Files:**
- Create: one-off data seed exposed via `StartingEquipmentOptions` admin page (Task 19) OR a `/organizace/hry/{gameId}/priprava-postav/seed-defaults` endpoint that inserts the 5 defaults

**5 defaults** (insert for the current Ovčina 2026 game after Phase 1 migrates):

| Key | DisplayName | Description |
|---|---|---|
| `tesak` | `Tesák (3/1)` | `Útok 3, obrana 1` |
| `dlouhy-nuz` | `Dlouhý nůž (2/2)` | `Útok 2, obrana 2` |
| `vrhaci-nuz` | `Vrhací nůž (3/0)` | `Útok 3, obrana 0` |
| `dyka-svitky` | `Dýka (1/2), 4 svitky kouzel` | `Útok 1, obrana 2 + 4 svitky kouzel` |
| `mince` | `5 měďáků / grošů` | `Žádná zbraň, 5 mincí do začátku` |

**Ask user to confirm the 5 rows verbatim before seeding.**

---

### Task 24: Version bump + PR

**Files:**
- Modify: `src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj` → `<Version>0.9.9</Version>` becomes `<Version>0.9.10</Version>`

**Steps:**
1. Bump version
2. Confirm full test suite passes at repo root: `dotnet test`
3. Start app locally via `run-app.bat`, walk through the manual QA checklist from design doc §7 / §11
4. Create feature branch `feature/character-prep` (all-lowercase per project convention)
5. **Ask user for permission to commit + push + open PR**
6. When approved: stage everything, commit with message `feat: character prep page + equipment options + email flow (v0.9.10)`, push, `gh pr create` with the design doc link in the body

---

## Parallelization map (for subagent dispatch)

```
Phase 1 (Tasks 1-4)  [MUST FINISH FIRST]
    │
    ├──► Phase 2 (Tasks 5-8)  [MUST FINISH BEFORE Phase 3-5,7-8]
    │       │
    │       ├──► Phase 3 (Tasks 9-10)  [email]  ─┐
    │       ├──► Phase 4 (Tasks 11-14) [page]   ─┤
    │       ├──► Phase 5 (Tasks 15-18) [admin]  ─┤  ALL PARALLELIZABLE
    │       ├──► Phase 7 (Task 20)     [excel]  ─┤
    │       └──► Phase 8 (Task 21)     [subdetail]┘
    │
    └──► Phase 6 (Task 19) [options CRUD]  PARALLELIZABLE WITH Phase 2 onward
    
Phase 9 Task 22 (E2E)  [LAST, after all above]
Phase 9 Task 23 (Seed) [after Task 19]
Phase 9 Task 24 (Bump+PR) [LAST-LAST]
```

**Recommended dispatch** (three concurrent subagents after Phase 2):
- Subagent A: Phases 3 (email) + 8 (submission detail integration) — these share the mail service
- Subagent B: Phases 4 (parent page) + 7 (excel) — independent, both read-only view consumers
- Subagent C: Phases 5 (dashboard) + 6 (config CRUD) — admin UI cluster

Each subagent owns a commit boundary at phase end, subject to user approval.
