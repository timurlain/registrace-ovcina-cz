# Ovčina Registration App — Visual Design Prompt

## Context

You are designing the visual theme and UI for **Ovčina**, a registration and operations web app for a 30-year-running outdoor fantasy LARP for children (ages 6–15) and families in the Czech Republic. The game is set in Tolkien's Rhovanion and has four player kingdoms. The app is built with ASP.NET Core / Blazor Server.

This is not a generic SaaS dashboard. It should feel like it belongs to the game world — warm, hand-crafted, grounded in nature — while remaining functional and fast for parents on mobile phones and organizers on desktop.

## Visual Identity

### Mood & Atmosphere

The game takes place in meadows and forests near a village in Moravia. Kids run around in cloaks and tabards, fight cardboard monsters, trade with city merchants, and discover hand-drawn maps. The aesthetic is:

- **Warm and earthy**, not cold or corporate
- **Hand-crafted feel**, not polished Silicon Valley
- **Fantasy-grounded**, not cartoonish — think "old map on a tavern table", not "mobile game"
- **Welcoming to families**, not intimidating — parents who have never played an RPG must feel comfortable
- **30 years of history** — this is a beloved community tradition, not a startup

### Existing Visual Language

The project already uses these visual elements across game cards, character sheets, and printed materials:

**Parchment texture** — aged beige/cream paper is the primary background treatment. The existing card system uses this exact texture for all game materials. It should appear in the app as a subtle background or surface treatment, not as an overwhelming wallpaper.

**Warm brown palette** (from character sheets):
- Dark brown `#2C1810` — primary text
- Saddle brown `#8B4513` — secondary text, labels
- Firebrick `#B22222` — accent, numbers, highlights
- Warm beige `#D4C4B0` — borders, grid lines, dividers
- Cream `#FFF8F0` — card/panel backgrounds
- Dark walnut `#3C2415` — header bars, section headers
- Parchment `#F5EDE3` — section backgrounds

**Burnt orange border** `#B26223` — used as card/panel border accent

**Kingdom colors** — these are canon and must be used consistently for kingdom-specific UI:

| Kingdom | Color | Hex suggestion | Used for |
|---------|-------|---------------|----------|
| Aradhryand (Elves) | Green | `#2E7D32` | Kingdom badges, assignment columns |
| Azanulinbar-Dum (Dwarves) | Red | `#C62828` | Kingdom badges, assignment columns |
| Esgaroth (Lake-people) | Blue | `#1565C0` | Kingdom badges, assignment columns |
| Nový Arnor (Mixed) | Yellow/Gold | `#F9A825` | Kingdom badges, assignment columns |

### Typography

The game uses **DejaVu** font family for all printed materials (required for Czech diacritics: č, ř, š, ž, ů, etc.). For the web app:

- **Headings:** Consider a serif or semi-serif with character — something like Merriweather, Lora, or Crimson Text. Should feel "book-like" or "chronicle-like", not modern sans-serif.
- **Body text:** Clean and readable. Inter, Source Sans, or similar. Must render Czech diacritics perfectly.
- **Monospace / data:** For admin tables and numbers, a clean mono like JetBrains Mono or Fira Code.
- **Avoid:** Generic system fonts, anything that looks like a Google Material template.

### Logo

The project has a logo (referenced on svet.ovcina.cz). Place it in the app header / nav bar. The logo should sit on the warm brown header bar (`#3C2415`).

## Page-by-Page Design Direction

### Public Landing Page

This is the first thing a parent sees after receiving an invitation email. It must:

- Show the game name and edition (e.g. "Ovčina — 30. ročník: Balinova pozvánka")
- Show dates and a brief atmospheric description
- Include a hero section with a **photo from a previous event** or the game map — not a stock photo
- Have a clear "Registrovat se" (Register) call-to-action
- Feel inviting and slightly magical, not bureaucratic
- On mobile: hero image + game name + dates + register button, nothing else above the fold

**Key image assets available:**
- Rhovanion fantasy map (parchment-bordered, detailed)
- Kingdom sanctuary illustrations (elven, dwarven, human, New Arnor)
- Hand-drawn building illustrations (medieval houses, villas, palaces in three kingdom styles)
- Parchment background textures
- Real event photos (to be provided by organizers for each edition)

### Login Page

- OAuth buttons (Microsoft, Google, Seznam) styled to match the warm theme, not default provider buttons
- Magic-link email input below
- Minimal, centered layout
- Parchment card on a subtle forest/nature background

### Registration Flow (Mobile-First)

The registration form is multi-step (household → attendees → food → summary → payment). Design it as a **wizard / stepper**:

- Step indicator at top (parchment tabs or a journey metaphor — "path markers on a map")
- Each step is one clear screen on mobile
- For each attendee, the form branches by role (Player vs NPC/Helper) — use subtle visual differentiation (e.g. a green-tinted card for players, a brown-tinted card for helpers)
- Character suggestion section should feel like opening an old journal — show previous character name, race, level in a styled "record card"
- Food ordering: simple checkboxes per meal per person, with running total visible
- Final summary: styled as a "scroll" or "letter" — list of all attendees, their roles, food, total price
- QR code page: prominent QR code on a clean background, bank details below, clear "Zaplaceno" (Paid) confirmation note

### Organizer Dashboard (Desktop-Biased)

Functional but warm. Not a generic admin panel.

- **Navigation:** Left sidebar with warm brown header bar. Icons for: Přehled (Overview), Registrace (Submissions), Lidé (People), Pošta (Inbox), Platby (Payments), Království (Kingdoms), Jídlo (Food), Nastavení (Settings)
- **Overview cards:** Registration counts, payment status, food summary — use the parchment card style with firebrick accent numbers
- **Tables:** Clean data tables with warm beige alternating rows, burnt orange header bar. Must be sortable and filterable.
- **Submission detail:** Unified timeline (notes, emails, payments, status changes) as a vertical feed on the right side of the detail page. Each event type has a distinct icon and subtle color.

### Kingdom Assignment Board (Drag & Drop)

This is the most visually distinctive admin page:

- Four columns, each **colored by kingdom** (green, red, blue, gold headers)
- Each player is a small card showing: name, age, level, previous kingdom badge
- Unassigned pool on the left or top
- Column stats (count, avg age, avg level) in the header of each kingdom column
- Drag interaction should feel like "moving tokens on a strategy map"
- Consider a subtle parchment/map background for this page specifically

### Integrated Inbox

- Email list on the left, detail on the right (standard email layout)
- Warm styling, not Outlook-clone
- Manual linking controls: dropdown to link email to a submission/person
- Organizer note input at bottom of each email detail

## Photography & Real-World Images

The app should prominently feature **real photos from previous Ovčina events**. These are not stock photos — they show real kids in costumes, forests, campfires, game props, handmade shields. The organizers will provide a curated set.

Suggested usage:
- Landing page hero (different photo per game edition)
- Login page background (blurred forest/game scene)
- Empty states ("No registrations yet" with a photo of kids preparing for adventure)
- About section or footer

**Do not use AI-generated images for the public-facing pages.** The charm is that this is real, handmade, 30 years of memories.

For admin/organizer pages, the fantasy art assets (sanctuary illustrations, creature art, map) can be used as subtle decorative elements — e.g. a small kingdom crest in the column header of the assignment board.

## Component Library Direction

Use a component library that can be themed warmly. Suggestions:

- **MudBlazor** (if Blazor) — highly customizable theme, supports custom palettes
- **Radzen** (if Blazor) — good data grids for admin tables
- **shadcn/ui** (if React) — fully customizable, no opinionated styling

Whatever is chosen, override the defaults aggressively. The default blue/gray of any component library will kill the atmosphere. Every surface should use the warm palette.

## CSS Custom Properties (Starting Point)

```css
:root {
  /* Core palette */
  --color-text-primary: #2C1810;
  --color-text-secondary: #8B4513;
  --color-accent: #B22222;
  --color-border: #D4C4B0;
  --color-surface: #FFF8F0;
  --color-surface-alt: #F5EDE3;
  --color-header: #3C2415;
  --color-header-text: #FFF8F0;
  --color-card-border: #B26223;

  /* Kingdom colors */
  --color-kingdom-elves: #2E7D32;
  --color-kingdom-dwarves: #C62828;
  --color-kingdom-lake: #1565C0;
  --color-kingdom-arnor: #F9A825;

  /* Status colors */
  --color-status-draft: #8B4513;
  --color-status-submitted: #1565C0;
  --color-status-paid: #2E7D32;
  --color-status-cancelled: #9E9E9E;

  /* Functional */
  --color-success: #2E7D32;
  --color-warning: #F9A825;
  --color-error: #C62828;
  --color-info: #1565C0;

  /* Typography */
  --font-heading: 'Merriweather', 'Georgia', serif;
  --font-body: 'Inter', 'Segoe UI', sans-serif;
  --font-mono: 'JetBrains Mono', 'Consolas', monospace;

  /* Spacing & radius */
  --radius-card: 8px;
  --radius-button: 6px;
  --shadow-card: 0 2px 8px rgba(44, 24, 16, 0.12);
}
```

## Responsive Breakpoints

- **Mobile (< 768px):** Registration flow is the primary concern. Single column, large touch targets, minimal chrome.
- **Tablet (768–1024px):** Hybrid — registration still works, organizer dashboard starts to open up.
- **Desktop (> 1024px):** Full organizer dashboard with sidebar, split panels, drag-and-drop kingdom board.

## What to Avoid

- Generic Material Design or Bootstrap blue/gray defaults
- Stock photography of any kind
- "Dark mode gaming" aesthetic — this is for families, not gamers
- Overly ornate fantasy borders that slow page load or hurt readability
- Any design that makes a non-technical parent feel like they've opened an admin panel
- Cookie-cutter SaaS dashboard layouts without theming

## What to Nail

- The parchment warmth must be immediately noticeable but not overwhelming
- Kingdom colors must be instantly recognizable across the app
- Mobile registration must be buttery smooth — three taps to start, no confusion
- The organizer dashboard must feel like "mission control for a fantasy event", not "enterprise HR software"
- Real photos from the event give the app its soul — make them prominent

## Available Asset Directories

The implementing agent can find visual assets at these paths:

- `games/2025 05 03 Ovčina S jídlem roste chuť/MagicalDeckLokaceJsouCajk/assets/images/` — fantasy art (creatures, locations, battlefields, buildings)
- `games/2025 05 03 Ovčina S jídlem roste chuť/MagicalDeckLokaceJsouCajk/assets/images/budovy/` — kingdom sanctuary illustrations
- `games/2025 05 03 Ovčina S jídlem roste chuť/MagicalDeckLokaceJsouCajk/assets/icons/` — game icons (weapons, stars, quests)
- `games/2026 05 01 Balinova pozvánka/ifp6ekov8kpg1.jpeg` — Rhovanion parchment map
- `sources/documents/05 Ovcina/Podzim/domy_ovcina.jpg` — hand-drawn kingdom buildings
- `sources/documents/Karticky/` — legacy item, creature, and weapon art
- `sources/documents/Obrázky/Lokace/` — location reference images

Event photos from previous years will be provided separately by the organizers.
