# Lodging Assignment Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Admin room catalog, per-game room config, and organizer room assignment page for indoor attendees.

**Architecture:** Master `Room` entity + `GameRoom` per-game link + `AssignedGameRoomId` on Registration. LodgingAssignmentService mirrors KingdomAssignmentService pattern. Organizer page reuses kingdom column layout.

**Tech Stack:** .NET 10, Blazor Server, EF Core, PostgreSQL

---

### Task 1: Data Model — Room, GameRoom, Registration FK

**Files:**
- Modify: `src/RegistraceOvcina.Web/Data/ApplicationModels.cs`
- Modify: `src/RegistraceOvcina.Web/Data/ApplicationDbContext.cs`

**Step 1: Add Room and GameRoom classes to ApplicationModels.cs**

At end of file alongside other model classes:

```csharp
public sealed class Room
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int DefaultCapacity { get; set; }
}

public sealed class GameRoom
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int RoomId { get; set; }
    public int Capacity { get; set; }
    public Game Game { get; set; } = default!;
    public Room Room { get; set; } = default!;
}
```

**Step 2: Add AssignedGameRoomId to Registration**

Find the `Registration` class (has `PreferredKingdomId`, `LodgingPreference`, etc.) and add:

```csharp
public int? AssignedGameRoomId { get; set; }
public GameRoom? AssignedGameRoom { get; set; }
```

**Step 3: Add DbSets and configuration to ApplicationDbContext.cs**

Add DbSets:

```csharp
public DbSet<Room> Rooms => Set<Room>();
public DbSet<GameRoom> GameRooms => Set<GameRoom>();
```

Add to `OnModelCreating`:

```csharp
builder.Entity<Room>(entity =>
{
    entity.HasKey(x => x.Id);
    entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
});

builder.Entity<GameRoom>(entity =>
{
    entity.HasKey(x => x.Id);
    entity.HasOne(x => x.Game).WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
    entity.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
    entity.HasIndex(x => new { x.GameId, x.RoomId }).IsUnique();
});
```

Add to the existing Registration entity configuration:

```csharp
entity.HasOne(x => x.AssignedGameRoom).WithMany().HasForeignKey(x => x.AssignedGameRoomId).OnDelete(DeleteBehavior.SetNull);
```

**Step 4: Generate migration**

```bash
cd src/RegistraceOvcina.Web
dotnet ef migrations add AddLodgingRoomAssignment
```

Verify: CreateTable for Rooms, GameRooms, AddColumn AssignedGameRoomId on Registrations, unique index on (GameId, RoomId).

**Step 5: Verify build**

```bash
dotnet build src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj
```

---

### Task 2: Room Catalog Admin Page

**Files:**
- Create: `src/RegistraceOvcina.Web/Components/Pages/Admin/Rooms.razor`
- Modify: `src/RegistraceOvcina.Web/Components/Layout/MainLayout.razor`

**Step 1: Create Rooms.razor**

Route: `@page "/admin/ubytovani"`
Auth: `@attribute [Authorize(Policy = AuthorizationPolicies.AdminOnly)]`
Rendermode: `@rendermode InteractiveServer`
Inject: `IDbContextFactory<ApplicationDbContext>`

Features:
- Table of rooms: Name, Default Capacity, action buttons (Edit, Delete)
- Inline create form: name input + capacity input + "Přidat" button
- Inline edit: name + capacity, Uložit/Zrušit
- Delete with confirmation
- Status messages

Pattern: Follow Roles.razor or Announcements.razor exactly.

**Step 2: Add nav link to MainLayout.razor**

In the Admin AuthorizeView block, add:

```html
<a class="nav-link" href="/admin/ubytovani">Ubytování</a>
```

**Step 3: Verify build**

```bash
dotnet build src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj
```

---

### Task 3: Per-Game Room Configuration

**Files:**
- Create: `src/RegistraceOvcina.Web/Components/Pages/Admin/GameRooms.razor`

**Step 1: Create GameRooms.razor**

Route: `@page "/admin/hry/{gameId:int}/ubytovani"`
Auth: `@attribute [Authorize(Policy = AuthorizationPolicies.AdminOnly)]`
Rendermode: `@rendermode InteractiveServer`
Inject: `IDbContextFactory<ApplicationDbContext>`

Features:
- Show game name at top, back link to /admin/hry
- Table of rooms linked to this game: Room Name, Capacity (editable), Remove button
- "Add room" dropdown: select from master Room catalog (exclude already linked), capacity defaults to Room.DefaultCapacity
- "Přidat všechny místnosti" button: links all unlinked rooms with default capacity
- Inline capacity edit per game room

**Step 2: Verify build**

```bash
dotnet build src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj
```

---

### Task 4: LodgingAssignmentService

**Files:**
- Create: `src/RegistraceOvcina.Web/Features/Lodging/LodgingAssignmentService.cs`
- Modify: `src/RegistraceOvcina.Web/Program.cs`

**Step 1: Create LodgingAssignmentService**

Mirror KingdomAssignmentService pattern. Key methods:

`GetAssignmentBoardAsync(int gameId)` — returns:
- Game name
- List of RoomColumns (GameRoomId, RoomName, Capacity, CurrentCount, List of LodgingCards)
- List of unassigned LodgingCards

Only loads registrations where:
- `Submission.GameId == gameId`
- `Submission.Status == Submitted`
- `Registration.Status == Active`
- `Registration.LodgingPreference == LodgingPreference.Indoor`

LodgingCard contains: RegistrationId, PersonName, BirthYear, GroupName, PersonNotes, AssignedGameRoomId

Cards sorted by GroupName then PersonName (keeps families together).

`AssignToRoomAsync(int registrationId, int? gameRoomId, string actorUserId)` — sets Registration.AssignedGameRoomId, validates registration is Indoor + Active + Submitted, creates AuditLog entry.

**Step 2: Register in DI**

In Program.cs, add:
```csharp
builder.Services.AddScoped<LodgingAssignmentService>();
```

**Step 3: Verify build**

```bash
dotnet build src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj
```

---

### Task 5: Organizer Room Assignment Page

**Files:**
- Create: `src/RegistraceOvcina.Web/Components/Pages/Organizer/LodgingAssignment.razor`

**Step 1: Create LodgingAssignment.razor**

Route: `@page "/organizace/hry/{gameId:int}/ubytovani"`
Auth: `@attribute [Authorize(Policy = AuthorizationPolicies.StaffOnly)]`
Inject: `LodgingAssignmentService`

Mirror KingdomAssignment.razor layout exactly:
- Back link to game management
- Title: "Ubytování — {GameName}"
- Total count of indoor attendees
- Column per room: header with name + current/capacity count, colored header
- "Nepřidělení" column for unassigned indoor attendees
- Cards with: name, age, group badge, notes on hover (reuse card pattern from KingdomAssignment)
- Drag-drop + reassign dropdown
- Hidden form for POST assignment (same JS pattern as kingdom)
- Status/error messages from query params

**Step 2: Create the POST endpoint**

Add to the Organizer endpoints (or inline in the page):
Route: `/organizace/hry/{gameId}/ubytovani/pridelit`
Method: POST with registrationId + gameRoomId form fields
Calls `LodgingAssignmentService.AssignToRoomAsync`
Redirects back with `?assigned=1` or `?error=...`

Pattern: Copy exactly from KingdomAssignment's `/pridelit` endpoint.

**Step 3: Verify build**

```bash
dotnet build src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj
```

---

### Task 6: Final verification + version bump

**Step 1: Full build**
```bash
dotnet build
```

**Step 2: Run E2E tests**
```bash
dotnet test tests/RegistraceOvcina.E2E/RegistraceOvcina.E2E.csproj
```

**Step 3: Check no pending migrations**
```bash
cd src/RegistraceOvcina.Web
dotnet ef migrations has-pending-model-changes
```

**Step 4: Bump version to 0.8.1**
In `src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj`, change `<Version>` to `0.8.1`.
