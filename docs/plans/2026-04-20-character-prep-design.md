# Character Prep — Design

> Date: 2026-04-20 | Status: approved, ready for implementation plan | Target game: Ovčina 2026 (start 2026-05-01)

## 1. Problem

On the morning of the LARP, 40 kids are handed a list of starting-equipment options and asked to choose one each. This blocks the opening of the game for 30+ minutes. In parallel, the organizers have a quiet data quality problem: the `Character` entity is not reliably linked back to the `Person` who plays it, because character names were collected informally and not systematically during registration.

**Goal:** pre-collect each Player's character name, starting-equipment choice, and an optional note before the game, so the start isn't blocked and the character ↔ person link is clean when OvčinaHra imports the data.

**Non-goal:** redesigning the submission flow, re-opening the parent-facing registration editor after the deadline, or building a full character-building experience.

## 2. Decisions (summary)

| # | Decision | Rationale |
|---|---|---|
| 1 | Fields per Player: character name + 1-of-5 starting equipment + optional note | Minimum viable, matches the on-site bottleneck |
| 2 | Equipment options stored in a per-game reference table | Rebalancing next year is data, not a deploy |
| 3 | Only write `Registration` fields on save — do not touch `Character` entity | Minimum blast radius. OvčinaHra already reads from `Registration`. |
| 4 | Two email templates: Pozvánka (full) + Připomínka (short, direct) | Natural two-beat flow; reminder doesn't need formality |
| 5 | Options schema is string-only — no stat columns | OvčinaHra is the stat engine; registrace just stores the pick |
| 6 | Access via opaque token on a dedicated anonymous page, one token per submission | Forwardable from parent to child; no login friction |
| 7 | Organizer dashboard with stats widget, filter, Excel export, per-row reminder | Real operational visibility |
| 8 | Nothing required; partial save allowed | Lowest friction; dashboard is the enforcement surface |
| 9 | Only `AttendeeType = Player` rows appear on the prep page | NPCs/helpers get gear from organizer crate |

## 3. Data model & migration

### 3.1 New table `StartingEquipmentOptions`

| Column | Type | Notes |
|---|---|---|
| `Id` | `Guid` PK | |
| `GameId` | `Guid` FK → `Games` | `OnDelete(Cascade)` |
| `Key` | `string(50)` | Stable identifier (e.g. `tesak`, `dyka-svitky`, `mince`). Unique per `GameId`. |
| `DisplayName` | `string(100)` | Rendered verbatim to parent (`Tesák (3/1)`) |
| `Description` | `string(500)?` | Nullable muted subtext |
| `SortOrder` | `int` | |

Unique filtered index on `(GameId, Key)`.

### 3.2 New columns on `Registrations`

| Column | Type | Notes |
|---|---|---|
| `StartingEquipmentOptionId` | `Guid?` FK → `StartingEquipmentOptions` | `OnDelete(Restrict)` — dropping an option must be deliberate |
| `CharacterPrepNote` | `string(4000)?` | Distinct from `RegistrantNote` (parent's note); never overwritten by parent edits to the submission editor |
| `CharacterPrepUpdatedAtUtc` | `DateTimeOffset?` | Stamped on every successful prep-page save |

`Registration.CharacterName` already exists (max 200) and is reused as-is. Pre-filled if already populated.

### 3.3 New columns on `RegistrationSubmissions`

| Column | Type | Notes |
|---|---|---|
| `CharacterPrepToken` | `string(64)?` | Base64Url-encoded 32 random bytes (~43 chars). Null until first pozvánka. Unique filtered index: `CharacterPrepToken IS NOT NULL`. |
| `CharacterPrepInvitedAtUtc` | `DateTimeOffset?` | Timestamp of first Pozvánka send; drives "Nezváno / Čeká" state |
| `CharacterPrepReminderLastSentAtUtc` | `DateTimeOffset?` | Timestamp of last Připomínka send; drives 24h throttle |

### 3.4 Migration

One EF migration: **`AddCharacterPrepAndStartingEquipment`**. Creates `StartingEquipmentOptions`, adds three columns to `Registrations`, adds three columns to `RegistrationSubmissions`, plus indexes.

Seed data for the 2026 game goes via a one-off organizer action on the `Konfigurovat výbavu` admin page, **not inside the migration**. Migration code must remain reusable for future games.

## 4. Token flow

### 4.1 Generation
First time the organizer triggers a pozvánka for a submission, `CharacterPrepToken` is populated via `RandomNumberGenerator.GetBytes(32)` + `Base64UrlEncode`. Stays null until then. Lazy — no background generation.

### 4.2 URL shape
`https://registrace.ovcina.cz/postavy/{token}`.

### 4.3 Lookup
Route `/postavy/{token}` anonymous Blazor Server page. Resolver does an indexed query by `CharacterPrepToken` (single column equality). Not found ⇒ 404 page with Czech copy: *"Odkaz již neplatí nebo je neznámý, kontaktujte organizátory."*

### 4.4 Lock
When `Game.StartDateUtc <= DateTimeOffset.UtcNow`, the page is rendered read-only: inputs disabled, save button hidden, banner *"Hra již začala, změny nelze provést."* Staff accessing through the dashboard bypass the lock.

### 4.5 Regeneration
Organizer button `Vygenerovat nový odkaz` on `SubmissionDetail.razor` replaces the token. Old link immediately 404s. Use case: link leaked, parent lost it, or household changed.

### 4.6 Auth model
Anonymous. Token *is* the auth. Equivalent to the existing `LoginToken` magic-link pattern but scoped to one submission's prep data. Token grants read + write only to: that submission's Player attendees' `CharacterName`, `StartingEquipmentOptionId`, `CharacterPrepNote`. No access to payments, inbox, other households.

### 4.7 Rate limiting
Light rate-limit by IP + token prefix. Reuse existing middleware if present; otherwise low priority — attack surface is narrow (sensitivity is low; write scope is narrow).

## 5. Parent-facing prep page

### 5.1 Route & layout
- Route: `/postavy/{token}`, Blazor Server, `[AllowAnonymous]`
- Minimal top chrome (no org sidebar) — this is a semi-public landing page
- Mobile-first — parents will open from phone

### 5.2 Structure
1. **Header** — *"Příprava postav pro {GameName}"* + 2-sentence explanation + collapsible help with option meanings.
2. **One card per Player attendee** (ordered by Person name, NPCs/helpers hidden):
   - Title: `{Person.FirstName} {Person.LastName}` (non-editable)
   - `Jméno postavy` — text input, max 200, pre-filled from `Registration.CharacterName`
   - `Startovní výbava` — radio group of the 5 options; `DisplayName` as label, `Description` as muted subtext; pre-selected from `StartingEquipmentOptionId`
   - `Poznámka` — 4-row textarea, max 4000, pre-filled from `CharacterPrepNote`; placeholder encourages free-text *"Cokoliv, co bychom měli vědět o postavě..."*
   - Muted timestamp *"Uloženo: {when}"* after successful save, sourced from `CharacterPrepUpdatedAtUtc`
3. **Single `Uložit` button** at the bottom. One server-side submit, one transaction for all rows. All fields optional. Trimmed whitespace. Toast *"Uloženo, díky"* on success.
4. **Read-only mode** when game has started — same cards, inputs disabled, save button hidden, banner up top.

### 5.3 Reuse
Form submit pattern matches `SubmissionEditor.razor`. Toast service, layout primitives, validation pattern all existing.

## 6. Email flow

### 6.1 Templates (both Czech, HTML via Graph outbound)

**Pozvánka** — full first invitation. Greeting, explain the bottleneck warmly, visual list of the 5 options with `DisplayName` + `Description`, big button-link to the prep URL + plain fallback URL, soft deadline (`Game.StartDateUtc - 3 days`), organizer contact, names of the household's Player attendees listed as confirmation. Subject: *"Příprava postav pro {GameName} — vyber startovní výbavu"*.

**Připomínka** — short, first-person, no branding fluff. 2–3 sentences: *"Ahoj, neviděli jsme tě ještě na stránce s přípravou postav. Odkaz je tady: {URL}. Díky! —Organizátoři"*. Subject: *"Připomínka: příprava postav pro {GameName}"*.

Both rendered server-side from razor/scriban or the existing template pattern.

### 6.2 Send surfaces

**Bulk** — dashboard page has two buttons:
- `Poslat pozvánku (X domácností)` — submissions where `CharacterPrepInvitedAtUtc IS NULL`
- `Poslat připomínku (Y domácností)` — submissions invited but with ≥1 Player row where `StartingEquipmentOptionId IS NULL`; disabled when Y=0

**Per-submission** — `SubmissionDetail.razor` grows a `Příprava postav` section with the same two buttons (scoped to this submission) + the current token (with copy-to-clipboard and a `Vygenerovat nový odkaz` link).

### 6.3 Throttle
Reminder send guard: if `CharacterPrepReminderLastSentAtUtc >= DateTimeOffset.UtcNow - 24h`, reject with UI error. Enforced server-side.

### 6.4 Outbox logging
Every send goes through the existing `EmailMessage` table with `LinkedSubmissionId` set, so organizers see history in the existing inbox/sent UI.

### 6.5 Post-game-start behavior
Both buttons disabled when `Game.StartDateUtc <= now`.

## 7. Organizer dashboard

### 7.1 Route & access
`/organizace/hry/{gameId}/priprava-postav` — staff-only (`[Authorize]` with existing staff policy).

### 7.2 Layout
1. **Stats strip** — 4 tiles (`Domácností celkem`, `Pozvaných`, `Plně vyplněných`, `Čeká na vyplnění`) matching GameStatsPage style.
2. **Action bar** — `Poslat pozvánku`, `Poslat připomínku`, `Stáhnout Excel`, `Konfigurovat výbavu`.
3. **Filter row** — status dropdown (`Všichni / Nezvaní / Čekající / Hotovo`) + full-text search over person and character name.
4. **Table** — one row per Player attendee:

   | Sloupec | Zdroj |
   |---|---|
   | Domácnost | `RegistrationSubmission.PrimaryContactName` → link to SubmissionDetail |
   | Hráč | `Person.FirstName + ' ' + LastName` |
   | Jméno postavy | `Registration.CharacterName` or `—` |
   | Výbava | `StartingEquipmentOption.DisplayName` or `—` (red indicator on empty) |
   | Poznámka | `CharacterPrepNote` truncated to 80 chars + tooltip |
   | Stav | Badge: `Nezváno` / `Čeká` / `Hotovo` |
   | Naposledy upraveno | Relative `CharacterPrepUpdatedAtUtc` |
   | Akce | `Otevřít odkaz`, `Poslat připomínku` |

5. **Sortable columns** matching the Payments page pattern.
6. **Optional row-expand** showing full note + send timeline (`Pozvaná {date}, připomenuta {date}`).

### 7.3 Staff override
`Otevřít odkaz` opens `/postavy/{token}` in a new tab. The page detects staff auth and bypasses the `Game.StartDateUtc` read-only lock.

### 7.4 Stats widget on `GameStatsPage`
Single tile: *"Příprava postav: {filled}/{total} vyplněno"* → linking to the dashboard.

### 7.5 Performance
One `IQueryable<RegistrationRowView>` projection joining `Registration → Person → RegistrationSubmission → StartingEquipmentOption`. Paginated 20/page. Fast even at 40–100 rows; designed to handle more.

### 7.6 Configuration sub-page
`Konfigurovat výbavu` (`/organizace/hry/{gameId}/priprava-postav/vybava`) — simple CRUD table over `StartingEquipmentOptions` for the current game: add, edit (DisplayName, Description, SortOrder), soft-delete (or hard-delete with confirmation if no `Registration` references). Seed button copies options from another game as a starting point.

## 8. Excel export

Endpoint: `GET /organizace/hry/{gameId}/priprava-postav/export.xlsx`. Mirrors the Kingdom XLSX pattern (`KingdomExportService.cs`). Service: `CharacterPrepExportService` at `Features/CharacterPrep/CharacterPrepExportService.cs`.

One sheet `Příprava postav`:

| Sloupec |
|---|
| Hráč |
| Rok narození |
| Jméno postavy |
| Startovní výbava |
| Poznámka |
| Domácnost |
| Email domácnosti |

One row per Player attendee (`AttendeeType = Player`), ordered by Person name. Includes everyone regardless of fill status. Empty cells stay empty.

Formatting: bold header, frozen row 1, autofilter, auto-width capped at 40 chars. Filename: `priprava-postav-{GameName-slug}-{yyyyMMdd}.xlsx`.

## 9. Testing strategy

### Unit
- `CharacterPrepServiceTests.cs` — EnsureTokenAsync idempotence, RotateTokenAsync invalidates, SaveAsync writes + timestamps, non-Player rows ignored, null-clearing allowed, game-start lock flag, bulk-invite filter, reminder filter, 24h throttle, Players-only filter
- `CharacterPrepExportServiceTests.cs` — bytes non-zero, valid XLSX, header row, row count, empty cells

### Integration (`WebApplicationFactory`)
- `GET /postavy/{valid}` 200 with Player names
- `GET /postavy/{unknown}` 404
- `GET /postavy/{rotatedOld}` 404
- Anonymous access allowed; no info leak from 404
- Excel endpoint requires staff

### E2E (Playwright)
- `CharacterPrepSmokeTests.cs`: organizer pozvánka → parent opens link → fills one row → saves → reloads → values pre-filled

### Snapshot (optional)
- Verify snapshot of rendered Pozvánka HTML for a fixed seed. Skip if Verify isn't already in csproj.

### Manual QA checklist (in PR description)
- Pozvánka bulk send creates 40 outbox rows with correct links
- Real email link resolves, save persists, reload pre-fills
- Staff prep link bypasses read-only lock after game start
- Excel encodes Czech chars correctly, column widths reasonable
- Regenerated token 404s old link

## 10. Out of scope (v1)

- SMS reminders (no infra; email only)
- Automatic scheduled sends (no cron for v1; organizers click)
- Auto-creating `Character` / `CharacterAppearance` entities (separate organizer workflow)
- Per-player tokens (one token per submission is forwardable and sufficient)
- Stat modeling in the options table (OvčinaHra is the stat engine)
- Configurable deadlines beyond `Game.StartDateUtc`

## 11. Open questions

- **Seed data for 2026 game** — who seeds the 5 rows for the May 1 game, and when? Recommendation: do it in the first PR via an organizer console action, not via migration. Document the 5 rows (DisplayName + Description) before merge.
- **OvčinaHra import** — how does OvčinaHra discover which loadout each player picked? Recommendation: extend the existing `/api/v1/games/{id}/adults`-style character seed endpoint with `StartingEquipmentKey` per registration. Small follow-up PR, not part of this design's scope.
