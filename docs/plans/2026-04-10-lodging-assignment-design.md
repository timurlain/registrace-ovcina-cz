# Lodging Assignment Design

## Problem

Organizers need to assign indoor-sleeping attendees to specific rooms. Currently lodging is just a preference enum on Registration — no room management or assignment exists.

## Solution

Master room catalog + per-game room linking + assignment on Registration. Admin page for room CRUD, organizer page for drag-drop assignment (same UX as kingdom assignment).

## Data Model

### Room (master catalog)

```
Room
- Id (int, PK)
- Name (string, max 100, required) — e.g. "Velká ložnice", "Malý pokoj"
- DefaultCapacity (int, required) — default bed count
```

Reusable across games. CRUD via admin page.

### GameRoom (per-game availability)

```
GameRoom
- Id (int, PK)
- GameId (int, FK → Game, required)
- RoomId (int, FK → Room, required)
- Capacity (int, required) — per-game override of DefaultCapacity
- unique index on (GameId, RoomId)
```

Links a room to a game with capacity override. Configured per game.

### Registration (existing, extend)

```
Registration (existing)
+ AssignedGameRoomId (int?, FK → GameRoom, nullable)
```

Simple nullable FK. No separate assignment entity — lodging is simpler than kingdoms.

## Admin Page: Room Catalog

**Route:** `/admin/ubytovani`
**Access:** Admin only

- Table of rooms with name + default capacity
- Create/edit/delete
- Simple CRUD, same pattern as kingdom management

## Per-Game Room Configuration

**Route:** `/admin/hry/{gameId}/ubytovani` or inline on game edit page

- Select which rooms are available for this game
- Override capacity per room if needed
- "Add all rooms" shortcut to link all master rooms with default capacity

## Organizer Page: Room Assignment

**Route:** `/organizace/hry/{gameId}/ubytovani`
**Access:** Staff only

### Layout

Same column layout as kingdom assignment:
- One column per GameRoom (room name + current/capacity count)
- "Unassigned" column for Indoor attendees not yet placed
- Only shows registrations with `LodgingPreference == Indoor`
- Cards show: name, age, group badge, notes on hover
- Drag-drop or reassign dropdown to move between rooms
- Group badge prominent — organizers want to keep families together

### Sorting

Cards sorted by group name first, then by name within group — makes it easy to see and keep families together.

## Navigation

- "Ubytování" link in admin nav (master room catalog)
- "Ubytování" link on per-game organizer pages (assignment)

## Migration

One migration adding:
- `Rooms` table
- `GameRooms` table with FK + unique index
- `AssignedGameRoomId` column on `Registrations`

## Out of Scope

- Outdoor/tent spot assignment (just headcount)
- Room pricing per-room (stays game-wide Indoor/Outdoor price)
- Attendee self-selection of room
- Room photos or descriptions
- Bed-level assignment within rooms
