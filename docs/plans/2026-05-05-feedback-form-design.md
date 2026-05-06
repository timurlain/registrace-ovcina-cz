# Post-Game Feedback Form — Design

**Date:** 2026-05-05
**Author:** Claude (registrace-ovcina-tinkerer) + Tomáš
**Status:** Approved, ready for implementation plan

## Problem

After a Game ends, organizers want structured reflection from every attendee — kids and adults — while memories are still fresh. The intended cadence is the same Saturday-evening exercise the kids who stayed at base did in person, replayed at home a few days later.

Five "core" questions came directly from that in-person session, plus two added by the user:

1. Jaký jsi měl nejsilnější zážitek ze hry, negativní/pozitivní?
2. Co byla tvá role ve hře, měl jsi něco, čím jsi se bavil (pracoval jsem pro krále, zabíjel nestvůry…)?
3. Letošní Ovčinu bylo pár změn (kroužky pro kouzelníky, dungeon, konec hry) — čeho sis všiml a co si o tom myslíš?
4. Máš nějaký příběh z letošní Ovčiny, který bychom mohli zapsat do kroniky?
5. Co si myslíte o Morii (jestli se líbilo nebo nelíbilo)?
6. Prosím cokoli dalšího, co tě napadá?
7. Příběh tvé postavy.

The above is the **kid** question set. Adults get a parallel-but-different set (organizers fill it in over time; v1 ships an empty placeholder list for adults).

The questions will change every game. The data model must absorb that without a schema migration each year.

## Non-goals (v1)

- Anti-spam / rate-limit on token submissions (Guid tokens are unguessable enough).
- Automatic chronicle compilation from pinned answers (just store the pin flag).
- AI summarization or sentiment scoring of responses.
- Per-Registration question overrides — everyone in a role gets the same set.
- Public chronicle page rendering — separate v2 effort.

## High-level shape

Mirrors the Character Prep feature pattern (`Features/CharacterPrep/`):

- Per-Registration token + invite/reminder bookkeeping on `Registration`.
- Question definitions live as JSON on `Game` (`FeedbackKidQuestionsJson`, `FeedbackAdultQuestionsJson`) so each game can evolve its questions without code changes.
- Answers live in a new `FeedbackResponses` table — one row per Registration, JSON payload keyed by `question.key`.
- Two access paths: public token link, and a logged-in registrator path (parent scribes for their kids).
- Organizer dashboard with stats, filterable rows, "Poslat pozvánky" / "Poslat připomínky" / "Otevřít / Uzavřít / Prodloužit" buttons, plus per-attendee read-only detail view.

## Data model

### `Game` — new columns

| Column | Type | Notes |
|---|---|---|
| `FeedbackKidQuestionsJson` | `text` (Postgres) / `nvarchar(max)` | Array of `{ key, label, helpText, placeholders[] }`. |
| `FeedbackAdultQuestionsJson` | `text` | Same shape, may be empty `[]` until adult set is curated. |
| `FeedbackOpensAtUtc` | `timestamptz?` | Default `null` ⇒ uses `EndsAtUtc` as effective open. |
| `FeedbackClosesAtUtc` | `timestamptz?` | Default `null` ⇒ uses `EndsAtUtc + 30 days` as effective close. |

JSON shape per question:

```json
{
  "key": "strongest_moment",
  "label": "Jaký jsi měl nejsilnější zážitek ze hry, negativní/pozitivní?",
  "helpText": "Klidně oba. Krátké poctivé věty stačí.",
  "placeholders": [
    "Když jsme v dungeonu...",
    "Smutné mi bylo, že...",
    "...",
    "..."
  ]
}
```

Answers are stored against `key`, so renaming a `label` next year does not orphan existing data.

### `Registration` — new columns

| Column | Type | Notes |
|---|---|---|
| `FeedbackToken` | `uuid?` UNIQUE | Per-attendee custom link. Generated lazily on first invitation. |
| `FeedbackInvitedAtUtc` | `timestamptz?` | Set when the attendee's token has been included in a sent email (kid-bundled or adult-individual). |
| `FeedbackReminderLastSentAtUtc` | `timestamptz?` | 24h throttle for reminder pass. |

### `FeedbackResponses` — new table

| Column | Type | Notes |
|---|---|---|
| `Id` | `int` PK | |
| `RegistrationId` | `int` FK → `Registrations(Id)` UNIQUE | One response per attendee per game. |
| `AnswersJson` | `text` | `{"strongest_moment": "...", "kingdom_role": "...", ...}` keyed by `question.key`. Empty object `{}` for not-yet-started rows we still want to track. |
| `LastEditedBy` | `int` (enum: `Self=0`, `HouseholdContact=1`, `Organizer=2`) | Audit signal — distinguishes the kid's own voice from a parent's paraphrase. |
| `LastEditedByPersonId` | `int?` FK → `Persons(Id)` | Identity of the typist when known (login path); null on token path with no associated Person. |
| `CreatedAtUtc` | `timestamptz` | |
| `UpdatedAtUtc` | `timestamptz` | Bumped on every save (including blur auto-save). |
| `SubmittedAtUtc` | `timestamptz?` | Set when "Hotovo, odeslat" is clicked. Drives Done status; user can keep editing afterward. |
| `PinToChronicle` | `bool` default `false` | Organizer-set in detail view. |
| `OrganizerNote` | `text?` | Free-form note visible only to organizers. |

### Status derivation

Mirrors `CharacterPrepStatus` semantics:

- `NotInvited` — `Registration.FeedbackInvitedAtUtc IS NULL`.
- `Done` — `FeedbackResponse.SubmittedAtUtc IS NOT NULL`.
- `Waiting` — invited but not submitted (or no `FeedbackResponse` row yet).
- `Closed` — derived only at view time when `now > EffectiveClosesAt`; not stored.

## Access paths

### Public token path

Route: `/zpetna-vazba/{registrationToken:guid}`

- Resolves `Registration` by `FeedbackToken` (404 on miss).
- No authentication required.
- Picks question set from `Game.FeedbackKidQuestionsJson` if `AttendeeType == Player`, else `FeedbackAdultQuestionsJson`.
- Open-window gate evaluated against `Game.FeedbackOpensAtUtc / ClosesAtUtc` falling back to `EndsAtUtc` and `EndsAtUtc + 30d`.
- `LastEditedBy = Self`, `LastEditedByPersonId = Registration.PersonId`.

### Logged-in scribe path

Route: `/registrace/{submissionId:int}/zpetna-vazba/{registrationId:int}`

- Auth: existing submission-ownership check (parent's logged-in account ↔ submission contact). Reuse the same authorization helper used by `/registrace/{id}` submission detail.
- Same form, same gate, same question set selection.
- `LastEditedBy = HouseholdContact` if the logged-in user is the submission contact, `Organizer` if logged in via the organizer scope, else `Self`.
- `LastEditedByPersonId` = the logged-in user's Person record (if linked).
- The submission detail page (`/registrace/{submissionId}`) gains a new card: "Zpětná vazba k Ovčině {GameName}" listing every Registration in the submission with status badge + "Vyplnit / Upravit" link.

### Organizer dashboard

Route: `/organizace/hry/{gameId:int}/zpetna-vazba`

- Stats tiles: Total attendees / Invited / Done / Waiting / Closed.
- Filterable, paged table: PersonFullName, Role (Player/Adult), Status, LastEditedBy badge, UpdatedAt, action links (Otevřít odpověď, Re-invite single).
- Buttons:
  - **Poslat pozvánky** — bulk to all `NotInvited` Registrations.
  - **Poslat připomínky** — to `Waiting` Registrations whose last reminder is older than 24h.
  - **Otevřít / Uzavřít zpětnou vazbu** — manually flip `Game.FeedbackOpensAtUtc / ClosesAtUtc`.
  - **Prodloužit o 30 dní** — `FeedbackClosesAtUtc = max(now, current) + 30d`.
- Per-Registration detail: `/organizace/hry/{gameId}/zpetna-vazba/{registrationId}` — read-only answers, pin-to-chronicle toggle, organizer-note textarea.

## Form UX

- One stacked card per question. Bold `label`, italic `helpText`, `<DxMemo>` body.
- `NullText` (placeholder) chosen at render time: `placeholders[Random.Shared.Next(placeholders.Length)]`. Refreshing the page yields a different placeholder; with 10 entries per question the variety stays unobtrusive.
- Empty `placeholders[]` falls back to `helpText` as `NullText`.
- Mobile-first: full-width memos, sticky footer, large tap targets.
- Footer buttons: **Uložit (rozepsáno)** / **Hotovo, odeslat** / last-saved timestamp.
- Auto-save on memo blur AND on Uložit click — both bump `UpdatedAtUtc` only.
- **Hotovo** sets `SubmittedAtUtc`; user can still edit afterward (we just stop counting them as "Waiting").

## Open / close gate

Effective open = `Game.FeedbackOpensAtUtc ?? Game.EndsAtUtc`.
Effective close = `Game.FeedbackClosesAtUtc ?? Game.EndsAtUtc + 30 days`.

| Condition | Behavior |
|---|---|
| `now < open` | Read-only banner: "Zpětná vazba se otevře po skončení hry." Form fields disabled. |
| `open ≤ now ≤ close` | Editable. |
| `now > close` | Read-only banner: "Zpětná vazba je uzavřena." Existing answers visible to that user; saves rejected. |

Organizer override (open / close / extend) writes the override to `Game.FeedbackOpensAtUtc` / `FeedbackClosesAtUtc` and the gate re-evaluates immediately.

## Email flow

Mirrors `Features/CharacterPrep/`:

- `IFeedbackEmailSender` — `SendInvitationAsync(Submission, IReadOnlyList<RegistrationToken>, CancellationToken)`, `SendReminderAsync(...)`.
- `GraphFeedbackEmailSender` — Microsoft Graph implementation.
- `UnconfiguredFeedbackEmailSender` — logs only, used when Graph is not configured (dev / preview).
- `FeedbackEmailRenderer` — Razor templates for invitation + reminder; kid-bundle (multi-link) vs adult-individual (single-link) variants.

### Recipient classification at send time

Iterate Registrations targeted by the bulk send. For each:

- **Adult with linked email** — `AttendeeType != Player` AND `Registration.Person.Email IS NOT NULL` → individual email to that address with their single token link.
- **Kid OR adult-without-email** → token gets bundled into a single consolidated email per Submission going to the household contact.

### Marking

After successful Graph send (mirroring CharacterPrep's "send-then-mark"):

- For invitation: every Registration whose token was included in a sent email gets `FeedbackInvitedAtUtc = nowUtc`.
- For reminder: every targeted Registration gets `FeedbackReminderLastSentAtUtc = nowUtc`.

## Excel export

Route: `/organizace/hry/{gameId}/zpetna-vazba/export`

- One row per Registration with a `FeedbackResponse` (or that has been invited).
- Columns: Person FirstName, Person LastName, AttendeeType, CharacterName, then one column per question (header = question.label), then Status, SubmittedAtUtc (local), LastEditedBy, PinToChronicle, OrganizerNote.
- Two sheets: "Děti" (kid question set) + "Dospělí" (adult question set), so each sheet has a stable column shape.
- Reuses whichever Excel library the project already depends on (CharacterPrep export will tell us — ClosedXML or EPPlus).

## Placeholder seeding for Game 1

- I'll fetch the Google Doc (`https://docs.google.com/document/d/1_Zkc-Ho8ZTYATQc6KNJTV6b0W62weRPTWjdsGfpKltc`) and curate **10 short, varied real answers per question** for the kid set.
- Migration includes a data step that, only if `FeedbackKidQuestionsJson IS NULL` for `Games.Id = 1`, sets the seeded JSON.
- Adult set seeded with the 7 question labels + empty `placeholders: []`. Organizer pastes curated examples after the first round.

## Testing

### Unit tests (`tests/RegistraceOvcina.Tests/Features/Feedback/`)

- `FeedbackServiceTests` — get/save view, soft-deleted submission excluded, cross-game spoofing rejected, AttendeeType-based question set selection, status derivation matrix.
- `FeedbackTokenServiceTests` — token resolves to Registration; expired/closed window returns read-only view; unknown token → 404.
- `FeedbackOptionsServiceTests` — JSON parse/validate round-trip; malformed JSON rejected with clear error.
- `FeedbackEmailRoutingTests` — adult-with-email gets individual; kid + adult-without-email bundle into contact email; reminder targets exclude Done.
- `FeedbackResponseAuditTests` — `LastEditedBy` set correctly per path (Self via token, HouseholdContact via login, Organizer via org scope).

### E2E tests (`tests/RegistraceOvcina.E2E/Feedback/`)

- `FillFeedbackViaTokenAsync` — public link, type 7 answers, click Hotovo, refresh sees Done.
- `ScribeFeedbackForChildAsync` — household contact logs in, opens submission, fills feedback for kid attendee, confirms `LastEditedBy = HouseholdContact`.
- `OrganizerInviteAndReminderFlowAsync` — dashboard invite → mail-pickup → token works; 24h reminder targeting respects `SubmittedAtUtc`.

### Migration sanity

- One migration: `AddFeedbackResponses` covering `FeedbackResponses` table + `Game.Feedback*` + `Registration.Feedback*` columns + indexes.
- `dotnet ef migrations has-pending-model-changes` returns false after generation.
- Unique index on `Registration.FeedbackToken` (filtered for non-null in Postgres).
- Unique index on `FeedbackResponses.RegistrationId`.

## Open questions / risks

- **PII in placeholders** — kid quotes from the Google Doc are anonymized as I curate (no names, no specifics that identify the speaker). I'll surface the curated 10×question for review before commit.
- **Multilingual labels** — for now Czech only. If we ever need English, the JSON shape supports adding a `labelEn` field without migration; deferred.
- **Anonymous re-fill** — once `SubmittedAtUtc` is set, the kid can keep editing. If we ever need a "this is final, lock it" state, add `LockedAtUtc`. Out of scope for v1.

## File-level surface

New / changed files (preview, not exhaustive):

- `src/RegistraceOvcina.Web/Features/Feedback/`
  - `FeedbackQuestion.cs` (record) + `FeedbackQuestionSet.cs`
  - `FeedbackOptionsService.cs` — JSON parse/serialize for Game JSON columns
  - `FeedbackService.cs` — get/save view, status, dashboard rows, invitation/reminder targets, mark methods
  - `FeedbackTokenService.cs` — token issue/resolve
  - `FeedbackViewModels.cs`
  - `IFeedbackEmailSender.cs` + `GraphFeedbackEmailSender.cs` + `UnconfiguredFeedbackEmailSender.cs` + `FeedbackEmailRenderer.cs`
  - `FeedbackExportService.cs` (Excel)
- `src/RegistraceOvcina.Web/Components/Pages/Feedback/Feedback.razor` (token + login form)
- `src/RegistraceOvcina.Web/Components/Pages/Organizer/FeedbackDashboard.razor` + `FeedbackResponseDetail.razor`
- `src/RegistraceOvcina.Web/Components/Pages/Submission/SubmissionDetail.razor` — add Zpětná vazba card
- `src/RegistraceOvcina.Web/Data/ApplicationModels.cs` — `FeedbackResponse`, new columns on `Game` and `Registration`, `FeedbackEditedBy` enum
- `src/RegistraceOvcina.Web/Data/ApplicationDbContext.cs` — DbSet + index config
- `src/RegistraceOvcina.Web/Migrations/<timestamp>_AddFeedbackResponses.cs`
- Tests as above.
