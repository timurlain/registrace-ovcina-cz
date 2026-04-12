# QR Stickers & Character Seed API — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add QR sticker print page and character seed API endpoint to registrace for the glejt/character system.

**Architecture:** Two additions — a new organizer page that generates printable QR sticker sheets using QRCoder (already installed), and a new integration API endpoint returning character data for a game.

**Tech Stack:** .NET 10, Blazor Server, QRCoder 1.6.0, existing integration API pattern

---

### Task 1: Character Seed API Endpoint

**Files:**
- Modify: `src/RegistraceOvcina.Web/Features/Integration/IntegrationApiEndpoints.cs`
- Modify: `src/RegistraceOvcina.Web/Features/Integration/IntegrationApiDtos.cs`

**Step 1: Add DTO to IntegrationApiDtos.cs**

```csharp
/// <summary>Character seed data for hra import.</summary>
public sealed record CharacterSeedDto(
    int CharacterId,
    int PersonId,
    string PersonFirstName,
    string PersonLastName,
    int PersonBirthYear,
    string CharacterName,
    string? Race,
    string? ClassOrType,
    string? KingdomName,
    int? KingdomId,
    int? LevelReached,
    string ContinuityStatus);
```

**Step 2: Add endpoint to IntegrationApiEndpoints.cs**

After the existing `/games/{id:int}/registrations` endpoint, add:

```csharp
// GET /api/v1/games/{id}/characters — character seeds for hra import
group.MapGet("/games/{id:int}/characters", async (
    int id,
    IDbContextFactory<ApplicationDbContext> dbFactory,
    CancellationToken ct) =>
{
    await using var db = await dbFactory.CreateDbContextAsync(ct);

    var characters = await db.CharacterAppearances
        .AsNoTracking()
        .Where(ca => ca.GameId == id && !ca.Character.IsDeleted)
        .Select(ca => new CharacterSeedDto(
            ca.CharacterId,
            ca.Character.PersonId,
            ca.Character.Person.FirstName,
            ca.Character.Person.LastName,
            ca.Character.Person.BirthYear,
            ca.Character.Name,
            ca.Character.Race,
            ca.Character.ClassOrType,
            ca.AssignedKingdom != null ? ca.AssignedKingdom.Name : null,
            ca.AssignedKingdomId,
            ca.LevelReached,
            ca.ContinuityStatus.ToString()))
        .ToListAsync(ct);

    return Results.Ok(characters);
});
```

**Step 3: Commit**

```bash
git add -A && git commit -m "feat: add character seed API endpoint for hra import"
```

---

### Task 2: QR Sticker Print Page

**Files:**
- Create: `src/RegistraceOvcina.Web/Components/Pages/Organizer/QrStickers.razor`
- Modify: `src/RegistraceOvcina.Web/Components/Layout/MainLayout.razor` (add nav link if game-specific pages have nav)

**Step 1: Create QrStickers.razor**

Route: `@page "/organizace/hry/{gameId:int}/qr-stitky"`
Authorization: organizer/admin only
Render mode: static SSR (print page, no interactivity needed)

The page:
1. Loads all active registrations for the game with their Person + Character data
2. Generates a QR code for each player encoding `hra.ovcina.cz/p/{personId}`
3. Renders a printable grid of stickers, each showing:
   - Player real name (FirstName LastName)
   - Character name
   - QR code (SVG inline via QRCoder)
4. CSS print layout: A4, grid of stickers (3 columns x N rows), cut lines
5. Print button at top (hidden in print CSS)

**QR generation using QRCoder:**
```csharp
using QRCoder;

private string GenerateQrSvg(int personId)
{
    var url = $"https://hra.ovcina.cz/p/{personId}";
    using var generator = new QRCodeGenerator();
    using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
    using var svgQr = new SvgQRCode(data);
    return svgQr.GetGraphic(3);
}
```

**Data loading:**
```csharp
var registrations = await db.Registrations
    .AsNoTracking()
    .Where(r => r.Submission.GameId == gameId
        && r.Submission.Status == SubmissionStatus.Submitted
        && r.Status == RegistrationStatus.Active
        && !r.Submission.IsDeleted
        && !r.Person.IsDeleted)
    .Select(r => new {
        r.Person.Id,
        r.Person.FirstName,
        r.Person.LastName,
        r.CharacterName
    })
    .OrderBy(r => r.LastName)
    .ThenBy(r => r.FirstName)
    .ToListAsync();
```

**Print CSS:**
```css
@media print {
    .no-print { display: none !important; }
    .sticker-grid { page-break-inside: avoid; }
    .sticker { 
        border: 1px dashed #ccc; 
        padding: 8px; 
        text-align: center;
        break-inside: avoid;
    }
}
```

**Step 2: Add navigation link**

On the game detail/management pages, add a link to the QR stickers page. Check where other game-specific organizer links live (like `/organizace/hry/{gameId}/kralovstvi` etc.) and add "QR štítky" link nearby.

**Step 3: Commit**

```bash
git add -A && git commit -m "feat: QR sticker print page for game badges"
```

---

### Task 3: Version Bump + PR

**Files:**
- Modify: `src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj` — bump to 0.8.9

**Step 1: Bump version**

**Step 2: Commit and push**

```bash
git add -A && git commit -m "[v0.8.9] feat: QR stickers and character seed API"
git push -u origin feature/qr-stickers-seed-api
```

**Step 3: Create PR, wait for CI, resolve comments, merge**
