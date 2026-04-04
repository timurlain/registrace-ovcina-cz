# Ovčina Registration Application — Revised Build Prompt

## Goal

Build an online registration and operations application for the Ovčina game.

This app replaces the old Google Form plus Excel-based workflow and becomes the operational source of truth for:

- registration
- household submissions
- returning attendee history
- character continuity
- kingdom planning
- food ordering
- payment tracking
- organizer communication

This is not just a one-person signup form. The real-world workflow is:

- one authenticated adult often registers multiple people at once
- children are usually players
- parents often participate as NPCs or helpers
- organizers need one place to see registration, communication, payments, notes, and history

Optimize for operational clarity and low admin overhead. Do not over-engineer.

## Product Principles

- Use one relational SQL database as the source of truth for all operational data.
- Excel and mailbox systems are inputs and references, not the authoritative store.
- Mobile-first for registrants. Desktop-biased for organizers/admins.
- UI language is Czech. Code, schema, APIs, and comments are in English.
- Include automated end-to-end testing for critical registrant and organizer flows.
- Prefer simple, manual, understandable workflows over clever automation.
- Keep data minimal. Birth year is enough; full birth date is not needed.
- Use soft-delete / archive semantics for core records, not hard delete by default.

## Recommended Technical Direction

- Backend: ASP.NET Core on .NET 10 if available, otherwise .NET 8 LTS.
- Frontend: prefer a single full-stack web app optimized for simplicity. Blazor Server is a good default for v1 because the app is form-heavy, admin-heavy, and low-scale. If React is chosen, justify why it is simpler for this project.
- Database: EF Core with SQL Server / Azure SQL.
- Hosting: Azure Web App.
- CI/CD: GitHub repository with GitHub Actions building, testing, and deploying to Azure.
- Email: Exchange Online shared mailbox plus Microsoft Graph integration.
- Payments: Czech QR payment standard (SPAYD).
- Testing: require automated E2E coverage for the critical happy paths and the most failure-prone operational flows.
- Architecture: single application, single database, no microservices.

## High-Level Product Scope

The application must support:

- authenticated registration before a draft can be created
- one household-style submission per game per registrant/contact
- multiple attendees inside one submission
- mixed roles in one submission, especially parents as NPC/helper roles and children as players
- person history across years
- read-only historical character and attendance data for registrants
- organizer/admin operations for people, registrations, inbox, payments, assignments, and imports
- a small read-only integration API for other Ovčina apps

## Explicitly In Scope for v1

- Microsoft login
- Google login
- Seznam login
- email OTP or magic-link fallback
- returning-user history recall
- household submissions with drafts
- per-attendee role-specific registration UI
- configurable per-game cutoffs
- manual kingdom assignment with drag and drop
- overall remaining player spots shown during registration
- payment QR generation
- manual payment confirmation
- game-configured meal options and prices
- integrated shared inbox page
- Excel import from historical files
- identity linking / person match review by staff
- basic audit trail
- simple organizer management
- narrow read-only integration API

## Explicitly Out of Scope for v1

- tracking pixels
- open/click email analytics
- automatic email intent parsing
- automatic kingdom assignment
- bank feed integration
- self-service person merge by registrants
- large external CRUD API
- multilingual UI
- attachment ingestion into app storage by default
- deep RPG systems such as quest progression, inventory, or narrative state

## Authentication and Identity

The application owns its own account model. Do not design this as "Microsoft Entra first" because Seznam support is required and should not be forced through an unsuitable federation path.

### Supported Sign-In Methods

- Microsoft
- Google
- Seznam
- email one-time passcode or magic-link fallback

### Important Constraint

Treat Seznam as a first-class provider requirement. If it requires custom OAuth2 integration, implement it directly in the app's authentication system.

### Login Rules

- Authentication is required before a user can start a registration draft.
- Invitation links are a fast path, not the only entry point.
- New families must also be able to start from the public landing page and log in normally.

### Roles

- `Registrant`: default role for normal users. Can create and edit own submission, view own history, and see payment instructions for own submissions.
- `Organizer`: operational role. Can review submissions, link people, read inbox, add notes, confirm payments, and manage kingdom assignments.
- `Admin`: everything Organizer can do, plus create games, configure settings, manage simple staff access, send invitations, and run imports.

### AppUser

Represents an authenticated application user.

Suggested fields:

- `Id`
- `Email`
- `DisplayName`
- `AuthProvider`
- `ProviderUserId`
- `PersonId` nullable
- `Role`
- `IsActive`
- `LastLoginAtUtc`
- `CreatedAtUtc`

Notes:

- `AppUser` is not the same thing as an attendee registration.
- A user may historically have been registered by a parent, then later become a direct registrant with their own account.
- Staff-only UI must exist for linking a newly authenticated adult to an older historical `Person`.

## Core Domain Model

### Person

Represents a real-world person across all years.

Suggested fields:

- `Id`
- `FirstName`
- `LastName`
- `BirthYear`
- `Email` nullable
- `Phone` nullable
- `Notes` nullable, internal only
- `IsDeleted`
- `CreatedAtUtc`
- `UpdatedAtUtc`

Rules:

- This is the cross-year identity.
- Matching across imports and new registrations must be conservative.
- Ambiguous matches go to organizer review, not silent merge.

### RegistrationSubmission

This is the household / contact-level aggregate and should replace the old loose `GroupName` idea.

One submission represents one registration batch for one game by one registrant/contact.

Suggested fields:

- `Id`
- `GameId`
- `RegistrantUserId`
- `PrimaryContactName`
- `PrimaryEmail`
- `PrimaryPhone`
- `Status` such as `Draft`, `Submitted`, `Cancelled`
- `SubmittedAtUtc` nullable
- `LastEditedAtUtc`
- `ExpectedTotalAmount`
- `RegistrantNote` nullable, visible to staff
- `IsDeleted`

Rules:

- Exactly one submission per game per registrant/contact.
- A submission can contain both players and organizer-side roles.
- Drafts must be supported.
- Registrants can return to drafts before final submission.
- Submitted submissions remain editable until relevant game cutoffs.
- After self-service registration closes, only organizers/admins can create or edit late submissions.

### Registration

Represents one attendee inside a submission for a specific game.

Suggested fields:

- `Id`
- `SubmissionId`
- `PersonId`
- `Role` such as `Player`, `Npc`, `Monster`, `TechSupport`
- `Status` such as `Active`, `Cancelled`
- `PreferredKingdomId` nullable
- `ContactEmail` nullable
- `ContactPhone` nullable
- `GuardianName` nullable
- `GuardianRelationship` nullable
- `GuardianAuthorizationConfirmed`
- `RegistrantNote` nullable
- `CreatedAtUtc`
- `UpdatedAtUtc`

Rules:

- Players and non-players share the same submission model but have different UI branches and validation.
- For minors, guardian data and confirmation are required.
- For adults, guardian fields are not required.
- `PreferredKingdom` is advisory only. It is not a reservation or promise.
- Older players may have their own email/phone even if the submission also has a household contact.

### Character

Long-lived character identity linked to a person.

Suggested fields:

- `Id`
- `PersonId`
- `Name`
- `Race` nullable
- `ClassOrType` nullable
- `Notes` nullable
- `IsDeleted`

### CharacterAppearance

Stores game-specific character facts.

Suggested fields:

- `Id`
- `CharacterId`
- `GameId`
- `RegistrationId`
- `LevelReached` nullable
- `AssignedKingdomId` nullable
- `ContinuityStatus` such as `Continued`, `Retired`, `Unknown`
- `Notes` nullable

Rules:

- Character continuity exists, but should be treated softly.
- For returning players, offer "continue previous character" as a suggestion, not an aggressive default.
- "New character" must be equally easy.
- Registrants see historical character data read-only.

### Game

Represents one run of the event.

Suggested fields:

- `Id`
- `Name`
- `Description`
- `StartsAtUtc`
- `EndsAtUtc`
- `RegistrationClosesAtUtc`
- `MealOrderingClosesAtUtc`
- `PaymentDueAtUtc`
- `AssignmentFreezeAtUtc` nullable
- `PlayerBasePrice`
- `AdultHelperBasePrice`
- `BankAccount`
- `BankAccountName`
- `VariableSymbolStrategy`
- `TargetPlayerCountTotal`
- `IsPublished`
- `CreatedAtUtc`
- `UpdatedAtUtc`

Rules:

- Use multiple cutoffs, not one global deadline.
- Adult helpers/NPCs default to zero game fee unless configured otherwise.
- Player price is usually applied to children/players.
- `VariableSymbolStrategy` should be a small configured strategy such as `PerSubmissionId`, `SequentialPerGame`, or `ManualOverride`.

### Kingdom

Use a canonical list of kingdoms, but assignment is game-specific operational data.

For each game, track target player counts per kingdom so the app can derive overall remaining capacity.

Rules:

- Show public overall remaining player spots during registration.
- Do not show public per-kingdom remaining spots as if they guarantee placement.
- Organizer kingdom assignment is manual only.

### MealOption and FoodOrder

`MealOption` is game-configured.

`FoodOrder` is per attendee, per day, and optional.

Rules:

- Meal choices are optional.
- Meal choices are attached to attendees, not only to the household.
- Meal ordering has its own cutoff and can lock earlier than the rest of registration.

### Payment

Payments should be event-based and auditable.

Suggested fields:

- `Id`
- `SubmissionId`
- `Amount`
- `Currency`
- `RecordedAtUtc`
- `RecordedByUserId`
- `Method`
- `Reference`
- `Note`

Rules:

- Do not overwrite historical payments when a submission changes.
- Keep immutable payment records.
- If a submitted registration changes after payment, recalculate expected total and derive current balance state such as `Unpaid`, `Underpaid`, `Balanced`, or `Overpaid`.
- Payment reconciliation is manual in v1.

### OrganizerNote

Internal organizer-only note attached to a submission, person, or other operational record.

Rules:

- Organizer notes are never visible to registrants.
- Keep submission notes from registrants separate from internal organizer notes.

### EmailMessage

Represents sent or received messages relevant to operations.

Suggested fields:

- `Id`
- `MailboxItemId`
- `Direction` such as `Inbound` or `Outbound`
- `From`
- `To`
- `Subject`
- `BodyText`
- `ReceivedAtUtc` or `SentAtUtc`
- `LinkedSubmissionId` nullable
- `LinkedPersonId` nullable
- `AttachmentMetadataJson` nullable

Rules:

- Store metadata, normalized text body, and attachment metadata in the app DB.
- Keep binary attachments in the mailbox for v1.
- Allow manual linking from email to submission/person.

### AuditLog

Track important staff actions such as:

- person linking / merging
- payment status changes
- kingdom assignment changes
- note edits
- game configuration changes
- staff role changes

### ImportProvenance

Low-priority but useful metadata for imported historical records.

Examples:

- source file
- source sheet
- source row or fingerprint
- imported timestamp

This is useful for debugging imports later, but can be implemented after core flows.

## Reference Data

Keep reference data lightweight and configurable.

The builder should assume the app needs:

- canonical kingdoms used for assignment and planning
- optional visual properties for kingdoms such as display name and color
- configurable suggestion lists for things like race and class/type

Rules:

- kingdoms should exist as canonical seeded or configured data
- race and class/type suggestions should help the UI but still allow free-text where appropriate
- do not turn reference data into a giant lore-management subsystem in v1

## Registration UX Rules

### Entry

- Public landing page must exist.
- Invitation link should fast-track the user into the correct game and prefill context where possible.
- Users can also arrive without an invitation.

### Drafts

- Users can start a draft, save it, and return later.
- Draft state must survive logout/session loss.

### Role-Specific Flow

The registration form must branch by role.

For players, include fields such as:

- character choice or suggestion from history
- preferred kingdom
- player-specific details

For NPC/helper roles, do not force player-only character fields.

### Mixed Household Submission

One submission can contain:

- children as players
- parents as NPCs/helpers
- other mixed attendees if needed

### Minors

For any attendee under 18:

- require guardian name
- require guardian relationship
- require explicit authorization confirmation by the submitting adult

### History and Prefill

Use previous attendance and character history to reduce friction:

- suggest previous characters
- suggest common contact information
- show prior attendance history

But:

- registrants see historical data read-only
- historical corrections go through organizers/admins

## Game Capacity and Kingdom Planning

Each game should define target player counts per kingdom.

Use this to:

- derive total target player count
- calculate overall remaining player spots
- support organizer balancing

### Public Registration Display

Show:

- total remaining player spots overall

Do not show:

- kingdom-specific public spot counts that imply a guaranteed placement

### Organizer Assignment

Provide a manual drag-and-drop assignment board.

Each player card should show enough context to balance kingdoms:

- name
- age derived from birth year
- previous kingdom if known
- previous level if known
- preferred kingdom

Each kingdom column should show:

- current count
- target count
- average age
- average level if available

This flow is manual only in v1. No auto-balancing algorithm.

## Payment Rules

### Pricing

Keep pricing simple and game-scoped.

For v1, support:

- base price for player/child registrations
- base price for adult helper/NPC registrations, usually zero
- per-meal prices

The submission total is the sum of its attendee and meal line items.

### User Experience

- Show live recalculated total during registration.
- Generate payment instructions and QR only after final submission, not during draft.
- Use SPAYD QR generation.

### Reconciliation

- Manual only in v1.
- Organizers/admins mark payments as received or adjust status.
- Personal bank account is acceptable for now.

## Email and Inbox

### Mailbox Backend

Assume v1 uses an Exchange Online shared mailbox such as `ovcina@ovcina.cz`.

### Sending

- Invitation and reminder emails should be sent from the same shared mailbox identity that receives replies.
- Use one visible email identity so the operational workflow stays consistent.

### Receiving

The app needs an integrated inbox page for organizers/admins.

The inbox should support:

- list of received messages
- message detail
- manual linking to submission/person
- organizer notes
- status updates

### Keep Inbox Simple

For v1:

- no tracking pixels
- no opened/clicked analytics
- no AI parsing or auto-updating registrations from email content
- no complex automation

### Unified Timeline

Each submission detail page should show a unified timeline containing:

- sent invitations and reminders
- incoming linked email messages
- organizer notes
- status changes
- payment events

## Organizer and Admin UI

### Organizer UI

Organizers need operational pages for:

- submission list and detail
- people list and detail
- integrated inbox
- payment review
- food summary
- kingdom assignment board
- identity linking review

### Admin UI

Admins additionally need:

- game creation and editing
- meal option configuration
- invitation sending
- simple organizer management
- import tools
- settings

### Organizer Management

Keep this intentionally simple in v1:

- list staff users
- grant or remove Organizer
- grant or remove Admin
- deactivate access

Do not turn this into a large workflow engine.

## Historical Import

The app must import historical data from:

- the old Google Form export
- the old Excel workflow, especially `OrganizaceLidi.xlsx`

### Import Goals

Import enough data to support:

- person history
- registrations by year
- roles
- character continuity where known
- kingdom history where known

### Import Style

- Import should be admin-triggered.
- Import should work per year / per logical batch.
- It is mainly an initial migration tool, not a normal yearly workflow.
- Make reruns safe enough for correction/recovery scenarios.

### Matching and Review

Matching must be conservative.

Auto-link only high-confidence cases. Everything ambiguous should surface as a staff review task in UI.

## Read-Only Integration API

Other Ovčina apps need a small, safe API.

### API Model

- read-only
- API key based
- per-client keys
- scoped access

### Acceptable API Use Cases

- is person registered for this game
- basic game roster facts
- basic person/character history needed by sibling apps

### Must Never Expose

- email
- phone
- guardian data
- payment details
- organizer-only notes

Do not turn this into a broad public CRUD API.

## Security, Privacy, and Data Rules

- Czech UI only in v1.
- Store all timestamps in UTC and display in Europe/Prague.
- Keep only necessary personal data.
- Birth year is enough for age estimation.
- Avoid collecting sensitive/special-category data unless a later explicit requirement demands it.
- Separate registrant-visible notes from staff-only notes.
- Use soft delete and explicit admin workflows for exceptional erasure cases.

## Suggested Implementation Order

### Phase 1 - Core Domain and Game Setup

- set up solution structure
- implement database and core entities
- implement `Game`, `Person`, `RegistrationSubmission`, `Registration`, `Character`, `CharacterAppearance`
- implement game cutoffs, kingdom targets, and pricing settings
- implement basic admin CRUD and audit logging foundation

### Phase 2 - Authentication and Registration

- implement app-owned auth with Microsoft, Google, Seznam, and email fallback
- require login before draft
- implement landing page and game entry
- implement draft household submissions
- implement role-based attendee forms
- implement history-based suggestions

### Phase 3 - Payments, Food, and Submission Detail

- implement meal options and per-attendee/day ordering
- implement live totals
- implement final submission flow
- implement SPAYD QR generation
- implement manual payment recording and balance status
- implement unified submission detail timeline

### Phase 4 - Admin Operations

- implement people review UI
- implement manual kingdom assignment board
- implement integrated inbox backed by shared mailbox
- implement identity linking review
- implement food summaries
- implement simple organizer management

### Phase 5 - Import and Integration

- implement historical import tool
- add import provenance metadata if needed
- implement narrow read-only integration API
- add polish, exports, and operational improvements

## Existing Infrastructure

- New repo: `timurlain/OvcinaRegistrace`
- Local repo root for bootstrapping: `C:\Users\TomášPajonk\source\repos\timurlain\registrace-ovcina-cz`
- Reference repo only: `timurlain/OvcinaSvet`
- Deploy separately from the current wiki / BookStack site
- Expected domain: subdomain or subpath under `ovcina.cz`, for example `registrace.ovcina.cz`

## Final Instruction to the Implementing Agent

Build the simplest system that correctly models the real workflow.

The biggest modeling truths are:

- one authenticated contact often registers multiple people
- one submission can mix parents/helpers and child players
- history matters across years
- payments, inbox, notes, and statuses belong to the same operational case view
- organizers need manual control more than automation

If in doubt, choose the design that makes staff operations clearer and makes household registration faster.
