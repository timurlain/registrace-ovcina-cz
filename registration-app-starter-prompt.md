# Ovčina Registration App — Starter Prompt for Codex

Build a production-oriented `v1` of the Ovčina registration app.

Use the detailed product spec in `registration-app-prompt.md` as the authoritative reference, but optimize for delivering a working, pragmatic first version quickly.

## Main Goal

Create a web application that replaces the old Google Form + Excel workflow for the Ovčina game.

The app must support:

- authenticated household registration
- multiple attendees in one submission
- mixed roles in one submission, especially children as players and parents as NPC/helpers
- returning attendee history across years
- manual kingdom assignment by organizers
- food ordering
- payment QR generation and manual payment confirmation
- integrated organizer inbox
- historical Excel import

## Product Shape

This is not a simple one-person signup form.

The primary real-world workflow is:

- an authenticated adult starts a registration
- they create one submission for one game
- they add multiple attendees to that submission
- some attendees are players
- some attendees are helpers / NPCs / organizers
- organizers later manage the submission as an operational case with notes, inbox messages, payment events, and assignment work

## Build Priorities

Prioritize these truths:

1. One submission per game per registrant/contact.
2. One submission can contain multiple attendees with different roles.
3. Registrants authenticate before they can start a draft.
4. Organizers need a single case view with timeline, notes, inbox, and payment state.
5. Keep workflows simple and manual unless automation is clearly required.
6. Add automated end-to-end tests for every critical user journey you build, especially registrant and organizer workflows.

## Local Repo

Start the project in this local repository path:

- `C:\Users\TomášPajonk\source\repos\timurlain\registrace-ovcina-cz`

Keep these prompt files in the repo root as bootstrap artifacts:

- `registration-app-starter-prompt.md`
- `registration-app-prompt.md`

## Recommended Stack

Default to this stack unless there is a strong reason not to:

- ASP.NET Core on .NET 10 if available, otherwise .NET 8 LTS
- Blazor Server for the UI
- EF Core
- SQL Server / Azure SQL
- ASP.NET Core Identity or equivalent app-owned auth model
- Microsoft Graph for Exchange Online mailbox integration
- SPAYD QR generation for payments
- Playwright for E2E browser coverage
- GitHub Actions for CI/CD to Azure

Use a single application and a single SQL database.

## Authentication

The app owns its account model.

Support these sign-in methods:

- Microsoft
- Google
- Seznam
- email OTP or magic-link fallback

Do not design the app around Microsoft Entra as the primary identity broker. Seznam support is a hard requirement, and if it requires direct integration, implement it directly.

## Core Entities

Implement these first:

- `AppUser`
- `Person`
- `Game`
- `RegistrationSubmission`
- `Registration`
- `Character`
- `CharacterAppearance`
- `Kingdom`
- `MealOption`
- `FoodOrder`
- `Payment`
- `OrganizerNote`
- `EmailMessage`
- `AuditLog`

## Required Domain Rules

### RegistrationSubmission

This is the household/contact aggregate.

- One submission belongs to one game.
- One submission belongs to one authenticated registrant/contact.
- One submission contains multiple attendee registrations.
- One submission is the unit for payment, communication, and organizer case handling.
- Support `Draft`, `Submitted`, and `Cancelled` states.

### Registration

Each attendee in the household gets their own registration row.

- A registration belongs to one submission.
- Roles must include at least `Player`, `Npc`, `Monster`, and `TechSupport`.
- Players can express a preferred kingdom.
- Preferred kingdom is advisory only, never guaranteed.
- Minors require guardian-related fields and confirmation.

### Person

`Person` is the cross-year real-world identity.

- Matching must be conservative.
- Ambiguous matches go to staff review.
- Returning children may later become directly authenticated adults without rewriting old household history.

### Character

Keep character handling simple.

- `Character` is the long-lived identity.
- `CharacterAppearance` stores per-game facts like level and kingdom.
- Show history read-only to registrants.
- Suggest previous character continuity, but do not force it.

### Game

Each game must support operational settings:

- registration cutoff
- meal ordering cutoff
- payment due date
- optional assignment freeze date
- player base price
- adult helper base price
- bank/payment settings
- target player counts per kingdom

## Registration UX

Implement a mobile-first registrant experience.

Requirements:

- public landing page
- login before starting a draft
- create or resume a draft submission
- add/edit/remove attendees
- role-specific form sections
- optional per-attendee, per-day meal choices
- live recalculated total
- final submission step
- payment instructions and QR shown only after submission, not during draft

Add E2E coverage for the key paths in this area, especially:

- authenticate
- create or resume a draft
- add or edit attendees
- submit
- view payment instructions

Use historical data to help the user:

- suggest prior character
- suggest contact data
- show prior attendance history

But keep historical data read-only for registrants.

## Kingdom Planning

Organizers must get a manual drag-and-drop kingdom assignment board.

Requirements:

- show unassigned players
- show kingdom columns
- show current count vs target count
- show average age
- show previous level if available
- show previous kingdom if available
- allow moving players between kingdoms manually

Do not implement auto-balancing in `v1`.

The public registration UI should show only total remaining player spots, not kingdom-specific availability.

## Payments

Keep payment handling simple and auditable.

Requirements:

- game-configured pricing
- children/players usually pay
- adult helpers/NPCs usually do not
- meal prices add to the submission total
- generate SPAYD QR after final submission
- record payment events immutably
- support manual payment confirmation by staff
- show `Unpaid`, `Underpaid`, `Balanced`, or `Overpaid` state

If a submitted registration changes after payment, do not overwrite historical payment records. Recalculate expected balance instead.

## Inbox

Include the inbox in `v1`.

Assume the mailbox backend is:

- Exchange Online shared mailbox
- for example `ovcina@ovcina.cz`

Requirements:

- sent invitations and reminders come from the same mailbox identity
- organizers can view inbound messages in the app
- organizers can manually link messages to a submission or person
- organizers can add notes and update statuses

Keep inbox behavior simple:

- no tracking pixels
- no open/click analytics
- no AI parsing
- no auto-updating registrations based on message content

Add E2E coverage for the highest-value organizer paths, especially:

- open submission detail
- review timeline
- process inbox-linked cases
- confirm payment
- assign kingdoms manually

## Unified Submission Detail

Each submission detail page should act as a case file and include:

- household contact info
- attendee list
- notes from the registrant
- internal organizer notes
- payment history and balance state
- linked inbox messages
- status changes
- audit-relevant events

## Admin and Organizer Pages

Implement at least these pages:

- public landing
- login
- registrant dashboard
- submission editor
- submission detail
- game list/detail
- people list/detail
- kingdom assignment board
- payment review
- food summary
- integrated inbox
- import page
- identity linking/review page
- simple organizer management page

Keep organizer management intentionally small:

- list staff users
- grant/remove `Organizer`
- grant/remove `Admin`
- deactivate access

## Historical Import

Import historical data from the existing Excel-based workflow.

Import goals:

- people
- registrations by year
- roles
- character history where possible
- kingdom history where possible

Treat import as an admin-triggered migration tool, mainly for initial migration, but make it safe enough to rerun for correction/recovery scenarios.

## Integration API

Expose a narrow read-only API for other Ovčina apps.

Use API keys with small scopes.

Allow only safe use cases such as:

- checking whether a person is registered for a game
- retrieving basic roster facts
- retrieving basic character/history facts needed by sibling apps

Do not expose:

- email
- phone
- guardian data
- payment data
- internal notes

## Data and Privacy Rules

- Czech UI only in `v1`
- code and schema in English
- store timestamps in UTC
- display dates/times in Europe/Prague
- use birth year, not full birth date
- avoid unnecessary sensitive data
- use soft delete / archive semantics by default

## Suggested Delivery Order

### Phase 1

Scaffold the solution and implement:

- base project structure
- database
- authentication shell
- `Game`, `Person`, `RegistrationSubmission`, `Registration`, `Character`, `CharacterAppearance`
- migrations
- basic admin CRUD
- initial Playwright setup with at least one smoke E2E test

### Phase 2

Implement the registrant flow:

- login
- draft submission
- attendee editing
- role-specific branching
- history suggestions
- final submission

### Phase 3

Implement operational features:

- meal ordering
- live pricing
- payment QR
- payment recording
- unified submission detail

### Phase 4

Implement organizer workflows:

- kingdom assignment
- inbox
- people review
- identity linking
- food summary

### Phase 5

Implement migration and integration:

- historical import
- read-only integration API
- polish and validation improvements

## Instruction Style

When making implementation decisions:

- prefer simpler architecture
- prefer explicit domain modeling over loose generic fields
- prefer manual organizer control over speculative automation
- preserve auditability
- optimize for a small real-world organizing team, not enterprise scale

Start by scaffolding the solution, defining the core entities and relationships, creating the initial migration, and building the first working vertical slice for:

- create game
- authenticate
- create draft household submission
- add attendees
- submit
- generate payment QR
