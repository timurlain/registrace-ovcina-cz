# QR Glejt & Character System — Design

**Date:** 2026-04-12
**Status:** Approved
**Deadline:** 2026-05-01 (game day)

## Problem

Ovčina LARP (~120 children) currently tracks character progression with pen and pencil on paper badges (glejty). No digital record of who leveled when, what skills were gained, or event history. Characters exist in registrace as seed data but don't live or evolve anywhere.

## Solution

Add QR codes to glejty (as stickers). Organizers scan with phone camera, land on hra.ovcina.cz, see/edit the character's live game state. Character management lives in hra as a natural extension of the world wiki.

## System Boundaries

### Registrace (registrace.ovcina.cz)
- **Owns:** Person, Registration, Character seed (name, class, kingdom from sign-up)
- **New:** QR sticker print page, character seed API endpoint
- **Does NOT:** track live game state

### OvcinaHra (hra.ovcina.cz)
- **Currently:** World wiki (locations, monsters, quests, items)
- **New:** Character entity, CharacterAssignment, event log, scan page
- **Owns:** Live character state (level, skills, points), cross-game character history
- **Auth:** OIDC via registrace (already working)

### Integration
- REST API only. Registrace exposes `GET /api/v1/games/{gameId}/characters` for seed data.
- Hra calls it once at game setup, creates its own records.
- No shared DB, no event bus, no file exports.

## Data Model (in Hra)

### Character (persistent world entity)
```
Id, Name, Race, Kingdom, BirthYear (in-game lore year)
Notes, IsPlayedCharacter (vs lore-only NPC)
IsDeleted, CreatedAtUtc, UpdatedAtUtc
ParentCharacterId (nullable — in-game family tree)
ExternalPersonId (nullable — registrace PersonId for played characters)
```

### CharacterAssignment (character in a game, played by a person)
```
Id, CharacterId, GameId, ExternalPersonId
IsActive (false if character died mid-game)
StartedAtUtc, EndedAtUtc (nullable)
```

- One Player (Person) can have multiple Characters over time
- One Character can participate in multiple Games (cross-game continuity)
- One Player can have multiple Characters in one Game (if character dies → new one)
- QR resolves: PersonId → active CharacterAssignment for current game → Character

### CharacterEvent (timestamped log)
```
Id, CharacterAssignmentId, Timestamp
OrganizerId (who logged it), EventType, Data (JSON)
Location (optional freeform text — which town/station)
```

Event types: `LevelUp`, `SkillGained`, `PointsChanged`, `Note`

Current character state = replay of events:
- Level = count of LevelUp events
- Skills = collected SkillGained events
- Points = sum of PointsChanged events per category (good/bad/neutral)

## QR Code

**Encodes:** `hra.ovcina.cz/p/{personId}` — simple URL, any phone camera works.

**PersonId** is from registrace, stable across games. Hra resolves which character this person is currently playing.

**Physical format:** Sticker sheet printed from registrace, peel and stick onto existing glejty. Each sticker shows player name + character name + QR.

## Scan Page UX (`/p/{personId}`)

Organizer scans QR → lands on scan page → sees:

**Header:** Character name, kingdom, race, level

**Quick actions (big buttons, mobile-friendly):**
- "Level up" — one tap
- "Add points" — pick category + amount
- "Add note" — freeform text

**Recent events:** last 5 entries with timestamp + organizer name

**No offline-first complexity.** Signal is mostly available, occasional dead spots. Failed writes retry in background. Character data cached in browser after first load (Blazor WASM).

## Registrace Changes

### 1. QR Sticker Print Page
- Route: `/organizace/hry/{gameId}/qr-stitky`
- Grid of stickers: player name + character name + QR code
- CSS print-optimized, cut along lines
- QRCoder NuGet for SVG generation
- Can ship independently before hra is ready

### 2. Character Seed API
- `GET /api/v1/games/{gameId}/characters`
- Returns all characters for a game with person info, kingdom, class
- Protected by existing API key filter

## Hra Changes

### 1. Character CRUD
- Admin pages for character list + edit form
- Fields: name, race, kingdom, birth year, notes, isPlayed, parent link
- Basic for May 1 — polish later

### 2. Import from Registrace
- Game setup action: call registrace API, create Character + CharacterAssignment records
- Match by ExternalPersonId to avoid duplicates on re-import

### 3. Scan Page
- `/p/{personId}` — mobile-optimized, organizer-only (OIDC auth required)
- Resolves active CharacterAssignment for current game
- Quick actions for level up, points, notes
- Event log display

### 4. Event Log
- CharacterEvent entity + EF migration
- Service for creating events + computing current state
- API endpoints for the Blazor WASM client

## Build Order (for May 1 deadline)

**Week 1 (Apr 12-18) — Registrace:**
1. QR sticker print page
2. Character seed API endpoint

**Week 1-2 (Apr 12-25) — Hra:**
1. Character + CharacterAssignment + CharacterEvent entities + migrations
2. Character CRUD pages (basic)
3. Import from registrace
4. Scan page + event log

**Week 3 (Apr 25-30) — Polish:**
1. Test with real data
2. Print stickers
3. Bug fixes

**Post-game (after May 1):**
- Family tree links UI
- Character history across games
- Richer skill/progression system
- Player-facing character profiles

## Non-Goals (for May 1)
- Offline-first PWA with sync
- Player-facing profiles (organizer-only)
- Skill catalog / progression rules
- Family tree UI (field exists, UI later)
- Character death / new character flow UI (can be done manually via admin)
