# Post-Game Feedback Form Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Ship a per-attendee post-game feedback form (kid + adult question sets, JSON-defined per Game) with public token + logged-in scribe access paths and an organizer dashboard for invites, reminders, status, pin-to-chronicle, and Excel export.

**Architecture:** Mirrors the existing `Features/CharacterPrep/` pattern: per-Registration token + invite/reminder bookkeeping, per-attendee `FeedbackResponse` row with JSON answers keyed by `question.key` from JSON columns on `Game`. Three pages: token form, login scribe form, organizer dashboard. Email routing classifies adult-with-email (individual) vs kid/no-email (bundled into household contact mail).

**Tech Stack:** .NET 10 + ASP.NET Core, Blazor Server, EF Core 9 + PostgreSQL, DevExpress Blazor (DxMemo), Microsoft Graph (Exchange Online), ClosedXML, xUnit, Playwright.

**Branch:** `feature/post-game-feedback` (already created, design doc committed at 630a3da).

**Reference docs:** `docs/plans/2026-05-05-feedback-form-design.md`, `Features/CharacterPrep/` for the mirror pattern.

**Process skills:**
- @superpowers:test-driven-development for every task with a Test step
- @superpowers:verification-before-completion before claiming a task done
- Migration discipline from `.claude/skills/registrace-ovcina-tinkerer/SKILL.md` — every model change → migration BEFORE commit
- Branch + PR — never merge without explicit user permission

---

## Task 1: Add data model + migration

**Files:**
- Modify: `src/RegistraceOvcina.Web/Data/ApplicationModels.cs` — add `FeedbackEditedBy` enum, `FeedbackResponse` entity, `Game.FeedbackKidQuestionsJson`, `Game.FeedbackAdultQuestionsJson`, `Game.FeedbackOpensAtUtc`, `Game.FeedbackClosesAtUtc`, `Registration.FeedbackToken`, `Registration.FeedbackInvitedAtUtc`, `Registration.FeedbackReminderLastSentAtUtc`.
- Modify: `src/RegistraceOvcina.Web/Data/ApplicationDbContext.cs` — add `DbSet<FeedbackResponse> FeedbackResponses`, configure unique indexes (`Registrations.FeedbackToken` filtered non-null, `FeedbackResponses.RegistrationId`), configure FK with `DeleteBehavior.Cascade` on `RegistrationId`.
- Create: `src/RegistraceOvcina.Web/Migrations/<timestamp>_AddFeedbackResponses.cs` (auto-generated)

**Step 1: Add enum + entity**

```csharp
// In ApplicationModels.cs

public enum FeedbackEditedBy
{
    Self = 0,
    HouseholdContact = 1,
    Organizer = 2,
}

public class FeedbackResponse
{
    public int Id { get; set; }
    public int RegistrationId { get; set; }
    public Registration Registration { get; set; } = null!;

    public string AnswersJson { get; set; } = "{}";

    public FeedbackEditedBy LastEditedBy { get; set; } = FeedbackEditedBy.Self;
    public int? LastEditedByPersonId { get; set; }
    public Person? LastEditedByPerson { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? SubmittedAtUtc { get; set; }

    public bool PinToChronicle { get; set; }
    public string? OrganizerNote { get; set; }
}
```

**Step 2: Add columns to Game**

```csharp
// On Game class:
public string? FeedbackKidQuestionsJson { get; set; }
public string? FeedbackAdultQuestionsJson { get; set; }
public DateTimeOffset? FeedbackOpensAtUtc { get; set; }
public DateTimeOffset? FeedbackClosesAtUtc { get; set; }
```

**Step 3: Add columns to Registration**

```csharp
// On Registration class:
public Guid? FeedbackToken { get; set; }
public DateTimeOffset? FeedbackInvitedAtUtc { get; set; }
public DateTimeOffset? FeedbackReminderLastSentAtUtc { get; set; }
public FeedbackResponse? FeedbackResponse { get; set; }
```

**Step 4: Configure DbContext**

```csharp
// In ApplicationDbContext.OnModelCreating, after existing config:

modelBuilder.Entity<FeedbackResponse>(b =>
{
    b.HasIndex(x => x.RegistrationId).IsUnique();
    b.HasOne(x => x.Registration)
        .WithOne(r => r.FeedbackResponse)
        .HasForeignKey<FeedbackResponse>(x => x.RegistrationId)
        .OnDelete(DeleteBehavior.Cascade);
    b.HasOne(x => x.LastEditedByPerson)
        .WithMany()
        .HasForeignKey(x => x.LastEditedByPersonId)
        .OnDelete(DeleteBehavior.SetNull);
    b.Property(x => x.AnswersJson).HasColumnType("text");
    b.Property(x => x.OrganizerNote).HasColumnType("text");
});

modelBuilder.Entity<Registration>()
    .HasIndex(x => x.FeedbackToken)
    .IsUnique()
    .HasFilter("\"FeedbackToken\" IS NOT NULL");

modelBuilder.Entity<Game>(b =>
{
    b.Property(x => x.FeedbackKidQuestionsJson).HasColumnType("text");
    b.Property(x => x.FeedbackAdultQuestionsJson).HasColumnType("text");
});
```

**Step 5: Generate migration**

```bash
cd src/RegistraceOvcina.Web
dotnet ef migrations add AddFeedbackResponses
```

Expected output: new file in `Migrations/` with `Up` containing `CreateTable("FeedbackResponses", ...)` + `AddColumn` × 7 (Game × 4 + Registration × 3) + 2 unique indexes.

**Step 6: Verify no drift**

```bash
dotnet ef migrations has-pending-model-changes
```

Expected: `No pending model changes.`

**Step 7: Verify build green**

```bash
dotnet build
```

Expected: Build succeeded. 0 Error(s).

**Step 8: Commit**

```bash
git add src/RegistraceOvcina.Web/Data/ApplicationModels.cs src/RegistraceOvcina.Web/Data/ApplicationDbContext.cs src/RegistraceOvcina.Web/Migrations/
git commit -m "feat(feedback): add FeedbackResponse entity + Game/Registration columns"
```

---

## Task 2: Question schema parser

**Files:**
- Create: `src/RegistraceOvcina.Web/Features/Feedback/FeedbackQuestion.cs`
- Create: `src/RegistraceOvcina.Web/Features/Feedback/FeedbackQuestionSet.cs`
- Create: `src/RegistraceOvcina.Web/Features/Feedback/FeedbackQuestionParser.cs`
- Test: `tests/RegistraceOvcina.Tests/Features/Feedback/FeedbackQuestionParserTests.cs`

**Step 1: Write the failing test**

```csharp
using RegistraceOvcina.Web.Features.Feedback;
using Xunit;

namespace RegistraceOvcina.Tests.Features.Feedback;

public class FeedbackQuestionParserTests
{
    [Fact]
    public void Parse_returns_empty_when_json_is_null_or_blank()
    {
        Assert.Empty(FeedbackQuestionParser.Parse(null).Questions);
        Assert.Empty(FeedbackQuestionParser.Parse("").Questions);
        Assert.Empty(FeedbackQuestionParser.Parse("   ").Questions);
    }

    [Fact]
    public void Parse_round_trips_well_formed_json()
    {
        const string json = """
        [
          {
            "key": "strongest_moment",
            "label": "Jaký jsi měl zážitek?",
            "helpText": "Krátké věty stačí.",
            "placeholders": ["a", "b", "c"]
          }
        ]
        """;

        var set = FeedbackQuestionParser.Parse(json);

        var q = Assert.Single(set.Questions);
        Assert.Equal("strongest_moment", q.Key);
        Assert.Equal("Jaký jsi měl zážitek?", q.Label);
        Assert.Equal("Krátké věty stačí.", q.HelpText);
        Assert.Equal(new[] { "a", "b", "c" }, q.Placeholders);
    }

    [Fact]
    public void Parse_throws_with_clear_message_on_invalid_json()
    {
        var ex = Assert.Throws<FormatException>(() => FeedbackQuestionParser.Parse("{not json}"));
        Assert.Contains("FeedbackQuestionSet", ex.Message);
    }

    [Fact]
    public void Parse_rejects_questions_with_blank_key()
    {
        const string json = """[{ "key": "", "label": "x" }]""";
        Assert.Throws<FormatException>(() => FeedbackQuestionParser.Parse(json));
    }

    [Fact]
    public void Parse_rejects_duplicate_keys()
    {
        const string json = """
        [
          { "key": "k", "label": "a" },
          { "key": "k", "label": "b" }
        ]
        """;
        Assert.Throws<FormatException>(() => FeedbackQuestionParser.Parse(json));
    }

    [Fact]
    public void Serialize_round_trips_back_to_parseable_json()
    {
        var set = new FeedbackQuestionSet(new[]
        {
            new FeedbackQuestion("k1", "L1", "H1", new[] { "p" }),
            new FeedbackQuestion("k2", "L2", null, Array.Empty<string>()),
        });

        var json = FeedbackQuestionParser.Serialize(set);
        var roundTripped = FeedbackQuestionParser.Parse(json);

        Assert.Equal(2, roundTripped.Questions.Count);
        Assert.Equal("k1", roundTripped.Questions[0].Key);
    }
}
```

**Step 2: Run test, expect fail**

Run: `dotnet test --filter FullyQualifiedName~FeedbackQuestionParserTests`
Expected: 6 failures, type does not exist.

**Step 3: Implement**

```csharp
// FeedbackQuestion.cs
namespace RegistraceOvcina.Web.Features.Feedback;

public sealed record FeedbackQuestion(
    string Key,
    string Label,
    string? HelpText = null,
    IReadOnlyList<string>? Placeholders = null);
```

```csharp
// FeedbackQuestionSet.cs
namespace RegistraceOvcina.Web.Features.Feedback;

public sealed class FeedbackQuestionSet
{
    public IReadOnlyList<FeedbackQuestion> Questions { get; }

    public FeedbackQuestionSet(IEnumerable<FeedbackQuestion> questions)
    {
        Questions = questions?.ToList() ?? new List<FeedbackQuestion>();
    }

    public static FeedbackQuestionSet Empty { get; } = new(Array.Empty<FeedbackQuestion>());
}
```

```csharp
// FeedbackQuestionParser.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RegistraceOvcina.Web.Features.Feedback;

public static class FeedbackQuestionParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static FeedbackQuestionSet Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return FeedbackQuestionSet.Empty;
        }

        List<RawQuestion>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<List<RawQuestion>>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new FormatException(
                $"Invalid FeedbackQuestionSet JSON: {ex.Message}", ex);
        }

        if (raw is null)
        {
            return FeedbackQuestionSet.Empty;
        }

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var questions = new List<FeedbackQuestion>(raw.Count);

        foreach (var r in raw)
        {
            if (string.IsNullOrWhiteSpace(r.Key))
            {
                throw new FormatException(
                    "FeedbackQuestionSet question is missing a non-blank 'key'.");
            }

            if (!seenKeys.Add(r.Key))
            {
                throw new FormatException(
                    $"FeedbackQuestionSet has duplicate question key '{r.Key}'.");
            }

            questions.Add(new FeedbackQuestion(
                r.Key,
                r.Label ?? string.Empty,
                string.IsNullOrWhiteSpace(r.HelpText) ? null : r.HelpText,
                r.Placeholders ?? Array.Empty<string>()));
        }

        return new FeedbackQuestionSet(questions);
    }

    public static string Serialize(FeedbackQuestionSet set)
    {
        var raw = set.Questions.Select(q => new RawQuestion
        {
            Key = q.Key,
            Label = q.Label,
            HelpText = q.HelpText,
            Placeholders = q.Placeholders?.ToArray(),
        }).ToList();

        return JsonSerializer.Serialize(raw, Options);
    }

    private sealed class RawQuestion
    {
        public string? Key { get; set; }
        public string? Label { get; set; }
        public string? HelpText { get; set; }
        public string[]? Placeholders { get; set; }
    }
}
```

**Step 4: Run tests, expect pass**

Run: `dotnet test --filter FullyQualifiedName~FeedbackQuestionParserTests`
Expected: 6 passed.

**Step 5: Commit**

```bash
git add src/RegistraceOvcina.Web/Features/Feedback/ tests/RegistraceOvcina.Tests/Features/Feedback/FeedbackQuestionParserTests.cs
git commit -m "feat(feedback): question schema parser + tests"
```

---

## Task 3: Feedback options service

**Files:**
- Create: `src/RegistraceOvcina.Web/Features/Feedback/FeedbackOptionsService.cs`
- Test: `tests/RegistraceOvcina.Tests/Features/Feedback/FeedbackOptionsServiceTests.cs`

**Step 1: Write the failing test**

```csharp
public class FeedbackOptionsServiceTests
{
    [Fact]
    public async Task Returns_kid_set_when_attendee_is_player()
    {
        await using var fixture = await TestDb.CreateAsync();
        var game = fixture.SeedGame(kidsJson: """[{"key":"k1","label":"K"}]""",
                                     adultsJson: """[{"key":"a1","label":"A"}]""");

        var sut = new FeedbackOptionsService(fixture.DbFactory);

        var set = await sut.GetForRoleAsync(game.Id, AttendeeType.Player, default);

        var q = Assert.Single(set.Questions);
        Assert.Equal("k1", q.Key);
    }

    [Fact]
    public async Task Returns_adult_set_for_non_player()
    {
        await using var fixture = await TestDb.CreateAsync();
        var game = fixture.SeedGame(kidsJson: """[{"key":"k1","label":"K"}]""",
                                     adultsJson: """[{"key":"a1","label":"A"}]""");

        var sut = new FeedbackOptionsService(fixture.DbFactory);

        var set = await sut.GetForRoleAsync(game.Id, AttendeeType.Helper, default);

        var q = Assert.Single(set.Questions);
        Assert.Equal("a1", q.Key);
    }

    [Fact]
    public async Task Returns_empty_set_when_json_is_null()
    {
        await using var fixture = await TestDb.CreateAsync();
        var game = fixture.SeedGame(kidsJson: null, adultsJson: null);

        var sut = new FeedbackOptionsService(fixture.DbFactory);

        var set = await sut.GetForRoleAsync(game.Id, AttendeeType.Player, default);

        Assert.Empty(set.Questions);
    }
}
```

> **Note on `TestDb`:** project already has a Postgres-backed test fixture (used by `CharacterPrepServiceTests`). If a `TestDb` doesn't yet exist with this exact shape, sub-task 3a is to add a `SeedGame` helper to the existing fixture. Look at `tests/RegistraceOvcina.Tests/TestUtilities/` for the convention.

**Step 2: Run test, expect fail**

Run: `dotnet test --filter FullyQualifiedName~FeedbackOptionsServiceTests`
Expected: type does not exist.

**Step 3: Implement service**

```csharp
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Feedback;

public sealed class FeedbackOptionsService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<FeedbackQuestionSet> GetForRoleAsync(
        int gameId,
        AttendeeType attendeeType,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var json = attendeeType == AttendeeType.Player
            ? await db.Games.AsNoTracking()
                .Where(x => x.Id == gameId)
                .Select(x => x.FeedbackKidQuestionsJson)
                .FirstOrDefaultAsync(cancellationToken)
            : await db.Games.AsNoTracking()
                .Where(x => x.Id == gameId)
                .Select(x => x.FeedbackAdultQuestionsJson)
                .FirstOrDefaultAsync(cancellationToken);

        return FeedbackQuestionParser.Parse(json);
    }

    public async Task<(FeedbackQuestionSet Kid, FeedbackQuestionSet Adult)> GetBothSetsAsync(
        int gameId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var row = await db.Games.AsNoTracking()
            .Where(x => x.Id == gameId)
            .Select(x => new { x.FeedbackKidQuestionsJson, x.FeedbackAdultQuestionsJson })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return (FeedbackQuestionSet.Empty, FeedbackQuestionSet.Empty);
        }

        return (
            FeedbackQuestionParser.Parse(row.FeedbackKidQuestionsJson),
            FeedbackQuestionParser.Parse(row.FeedbackAdultQuestionsJson));
    }
}
```

**Step 4: Run tests, expect pass**

Run: `dotnet test --filter FullyQualifiedName~FeedbackOptionsServiceTests`
Expected: 3 passed.

**Step 5: Commit**

```bash
git add src/RegistraceOvcina.Web/Features/Feedback/FeedbackOptionsService.cs tests/RegistraceOvcina.Tests/Features/Feedback/FeedbackOptionsServiceTests.cs
git commit -m "feat(feedback): options service + tests"
```

---

## Task 4: Feedback token service

**Files:**
- Create: `src/RegistraceOvcina.Web/Features/Feedback/FeedbackTokenService.cs`
- Test: `tests/RegistraceOvcina.Tests/Features/Feedback/FeedbackTokenServiceTests.cs`

**Step 1: Write failing tests**

```csharp
public class FeedbackTokenServiceTests
{
    [Fact]
    public async Task EnsureToken_assigns_a_new_guid_when_none_exists()
    {
        await using var fixture = await TestDb.CreateAsync();
        var reg = fixture.SeedPlayerRegistration();

        var sut = new FeedbackTokenService(fixture.DbFactory);
        var token = await sut.EnsureTokenAsync(reg.Id, default);

        Assert.NotEqual(Guid.Empty, token);

        var roundTripped = await sut.EnsureTokenAsync(reg.Id, default);
        Assert.Equal(token, roundTripped);
    }

    [Fact]
    public async Task ResolveToken_returns_registration_id_for_known_token()
    {
        await using var fixture = await TestDb.CreateAsync();
        var reg = fixture.SeedPlayerRegistration();

        var sut = new FeedbackTokenService(fixture.DbFactory);
        var token = await sut.EnsureTokenAsync(reg.Id, default);

        var resolved = await sut.ResolveAsync(token, default);
        Assert.Equal(reg.Id, resolved);
    }

    [Fact]
    public async Task ResolveToken_returns_null_for_unknown_token()
    {
        await using var fixture = await TestDb.CreateAsync();
        var sut = new FeedbackTokenService(fixture.DbFactory);

        var resolved = await sut.ResolveAsync(Guid.NewGuid(), default);
        Assert.Null(resolved);
    }
}
```

**Step 2: Run, expect fail**

**Step 3: Implement**

```csharp
public sealed class FeedbackTokenService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<Guid> EnsureTokenAsync(int registrationId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var reg = await db.Registrations.FirstOrDefaultAsync(x => x.Id == registrationId, cancellationToken)
            ?? throw new InvalidOperationException($"Registration {registrationId} not found.");

        if (reg.FeedbackToken is null)
        {
            reg.FeedbackToken = Guid.NewGuid();
            await db.SaveChangesAsync(cancellationToken);
        }

        return reg.FeedbackToken.Value;
    }

    public async Task<int?> ResolveAsync(Guid token, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Registrations
            .AsNoTracking()
            .Where(x => x.FeedbackToken == token && !x.Submission.IsDeleted)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
```

**Step 4: Run, expect pass**

**Step 5: Commit**

```bash
git commit -m "feat(feedback): token service + tests"
```

---

## Task 5: Feedback view + save service (heart of the feature)

**Files:**
- Create: `src/RegistraceOvcina.Web/Features/Feedback/FeedbackViewModels.cs`
- Create: `src/RegistraceOvcina.Web/Features/Feedback/FeedbackService.cs`
- Test: `tests/RegistraceOvcina.Tests/Features/Feedback/FeedbackServiceTests.cs`

**Sub-tasks (each its own commit):**

### 5a — `GetViewAsync` returns attendee + question set + read-only flag

ViewModel:

```csharp
public sealed record FeedbackView(
    int RegistrationId,
    int SubmissionId,
    int GameId,
    string GameName,
    string AttendeeFullName,
    AttendeeType AttendeeType,
    FeedbackQuestionSet Questions,
    IReadOnlyDictionary<string, string> Answers,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset EffectiveOpensAt,
    DateTimeOffset EffectiveClosesAt,
    bool IsReadOnly,
    string? ReadOnlyReason);
```

Tests cover: returns view for valid registration; null for unknown id; null for soft-deleted submission; read-only when before opens-at; read-only when after closes-at; effective open/close defaults to `EndsAtUtc` / `EndsAtUtc + 30d` when game has nulls; honours overrides when set.

### 5b — `SaveAsync(registrationId, answers, editedBy, editedByPersonId)` persists + audit

Tests: creates new FeedbackResponse on first save; updates existing on subsequent save; audit fields set correctly; rejects save when window closed (read-only); rejects unknown question keys (silent ignore — protects against schema renames); ignores blank answers (don't overwrite existing with blank).

### 5c — `MarkSubmittedAsync` sets `SubmittedAtUtc`

Tests: sets timestamp; idempotent on re-submit (keeps original).

### 5d — Status derivation

```csharp
public enum FeedbackStatus { NotInvited, Waiting, Done }

public async Task<FeedbackStatus?> GetStatusAsync(int registrationId, CancellationToken ct);
```

Tests: NotInvited when InvitedAtUtc null; Done when SubmittedAtUtc not null; Waiting otherwise.

**Commit per sub-task** (4 commits in this task).

---

## Task 6: Dashboard service

**Files:**
- Modify: `src/RegistraceOvcina.Web/Features/Feedback/FeedbackService.cs`
- Test: `tests/RegistraceOvcina.Tests/Features/Feedback/FeedbackDashboardTests.cs`

**Step 1: Write failing tests**

Cover:
- `GetDashboardStatsAsync(gameId)` — Total / Invited / Done / Waiting counts; soft-deleted submissions excluded.
- `GetDashboardRowsAsync(gameId, filter, page, pageSize)` — paginated, sortable, filterable by status / role / search; sort: Status asc, then Submission contact name, then Person name.
- `ListInvitationTargetsAsync(gameId)` — only `FeedbackInvitedAtUtc IS NULL`.
- `ListReminderTargetsAsync(gameId, nowUtc)` — invited, no SubmittedAtUtc, last reminder > 24h ago.
- `MarkInvitedAsync` / `MarkReminderSentAsync` — single-Registration timestamp updates, idempotent for invitation.

**Step 2-4: Implement service methods + run tests**

**Step 5: Commit**

```bash
git commit -m "feat(feedback): dashboard service + invitation/reminder targets"
```

---

## Task 7: Email sender + renderer

**Files:**
- Create: `src/RegistraceOvcina.Web/Features/Feedback/IFeedbackEmailSender.cs`
- Create: `src/RegistraceOvcina.Web/Features/Feedback/UnconfiguredFeedbackEmailSender.cs`
- Create: `src/RegistraceOvcina.Web/Features/Feedback/GraphFeedbackEmailSender.cs`
- Create: `src/RegistraceOvcina.Web/Features/Feedback/FeedbackEmailRenderer.cs`
- Create: `src/RegistraceOvcina.Web/Features/Feedback/FeedbackEmailViewModels.cs`
- Create: `src/RegistraceOvcina.Web/Features/Feedback/Templates/FeedbackInvitationContactBundle.cshtml`
- Create: `src/RegistraceOvcina.Web/Features/Feedback/Templates/FeedbackInvitationAdultIndividual.cshtml`
- Create: `src/RegistraceOvcina.Web/Features/Feedback/Templates/FeedbackReminderContactBundle.cshtml`
- Create: `src/RegistraceOvcina.Web/Features/Feedback/Templates/FeedbackReminderAdultIndividual.cshtml`
- Test: `tests/RegistraceOvcina.Tests/Features/Feedback/FeedbackEmailRoutingTests.cs`

**Step 1: Mirror CharacterPrep email surface**

```csharp
public interface IFeedbackEmailSender
{
    Task SendContactBundleAsync(FeedbackContactBundleEmail email, CancellationToken ct);
    Task SendAdultIndividualAsync(FeedbackAdultIndividualEmail email, CancellationToken ct);
}
```

`FeedbackContactBundleEmail` carries: ToEmail, ContactName, GameName, FeedbackClosesAtLocal, list of `(AttendeeName, TokenLink)` pairs, IsReminder bool.
`FeedbackAdultIndividualEmail` carries: ToEmail, AttendeeName, GameName, FeedbackClosesAtLocal, TokenLink, IsReminder bool.

**Step 2: Routing classifier (pure function, easy to test)**

```csharp
public static class FeedbackEmailRouter
{
    public static (FeedbackContactBundleEmail? bundle, IReadOnlyList<FeedbackAdultIndividualEmail> individuals) Classify(
        Submission submission,
        IReadOnlyList<Registration> registrations,
        Game game,
        Func<int, string> tokenLinkFor,
        bool isReminder);
}
```

Tests:
- Player attendee → goes into bundle.
- Adult attendee with email → goes into individuals.
- Adult attendee without email → goes into bundle.
- Submission with only adults-with-emails → bundle is null.
- Submission with no contact email → throws (caller must pre-filter).

**Step 3: Templates** — Razor `.cshtml` files rendered via `RazorViewToStringRenderer` (existing pattern in `Features/CharacterPrep/CharacterPrepEmailRenderer.cs`).

**Step 4-5: Build, run tests, commit**

```bash
git commit -m "feat(feedback): email sender + routing + templates"
```

---

## Task 8: Token form page

**Files:**
- Create: `src/RegistraceOvcina.Web/Components/Pages/Feedback/Feedback.razor`
- Create: `src/RegistraceOvcina.Web/Components/Pages/Feedback/Feedback.razor.cs` (code-behind)

**Step 1: Page route + skeleton**

```csharp
@page "/zpetna-vazba/{Token:guid}"
@inject FeedbackTokenService TokenService
@inject FeedbackService FeedbackService
@inject FeedbackOptionsService OptionsService
@inject TimeProvider Time

<PageTitle>Zpětná vazba — @_view?.GameName</PageTitle>

@if (_view is null)
{
    <p>Odkaz nenalezen.</p>
    return;
}

<FeedbackForm View="_view"
              OnSave="HandleSaveAsync"
              OnSubmit="HandleSubmitAsync" />
```

**Step 2: Code-behind** — resolve token → registration id → load FeedbackView with `LastEditedBy = Self` and `LastEditedByPersonId = Registration.PersonId`.

**Step 3: `FeedbackForm` component** — receives `FeedbackView`, renders one `<DxMemo>` per question with rotating placeholder. Uses `Random.Shared` per render to pick `Placeholders[i]`. Two buttons: Uložit + Hotovo, odeslat.

**Step 4: Manual smoke test**

```
1. dotnet run
2. Seed a Registration via dev seeder, copy its FeedbackToken
3. Navigate /zpetna-vazba/{token}
4. Fill in answers, click Uložit, refresh
5. Verify answers persisted, placeholder rotated on refresh
6. Click Hotovo, refresh, see Done badge logic somewhere
```

**Step 5: Commit**

```bash
git commit -m "feat(feedback): public token form page + DxMemo rotating placeholders"
```

---

## Task 9: Logged-in scribe path

**Files:**
- Create: `src/RegistraceOvcina.Web/Components/Pages/Feedback/FeedbackScribe.razor` at route `/registrace/{SubmissionId:int}/zpetna-vazba/{RegistrationId:int}`
- Modify: `src/RegistraceOvcina.Web/Components/Pages/Submission/SubmissionDetail.razor` (or whatever the existing detail page is — check repo) to add the "Zpětná vazba" card

**Step 1: Auth guard**

Resolve current user → linked Person → all Submissions where Person is contact. Reject if requested SubmissionId is not in that set. Reuse existing `ISubmissionAuthorizationService` if one exists; otherwise inline the check (and add a TODO to factor out).

**Step 2: Card on submission detail**

```razor
@if (_feedbackOpen)
{
    <Card Title="Zpětná vazba k Ovčině @GameName">
        @foreach (var attendee in _attendeesWithStatus)
        {
            <div>
                <span>@attendee.PersonFullName</span>
                <FeedbackStatusBadge Status="attendee.Status" />
                <a href="/registrace/@SubmissionId/zpetna-vazba/@attendee.RegistrationId">
                    @(attendee.Status == FeedbackStatus.Done ? "Upravit" : "Vyplnit")
                </a>
            </div>
        }
    </Card>
}
```

**Step 3: Same `FeedbackForm` component, different LastEditedBy**

Pass `editedBy = FeedbackEditedBy.HouseholdContact` and `editedByPersonId = currentUserPersonId` into the save call.

**Step 4: Commit**

```bash
git commit -m "feat(feedback): logged-in scribe path + submission detail card"
```

---

## Task 10: Organizer dashboard

**Files:**
- Create: `src/RegistraceOvcina.Web/Components/Pages/Organizer/FeedbackDashboard.razor`
- Create: `src/RegistraceOvcina.Web/Components/Pages/Organizer/FeedbackResponseDetail.razor`

**Step 1: Dashboard route `/organizace/hry/{gameId:int}/zpetna-vazba`**

- Stats tiles row.
- DxGrid with columns: Person, Role, Status, LastEditedBy, UpdatedAt, Actions.
- Buttons (top of page): Poslat pozvánky / Poslat připomínky / Otevřít / Uzavřít / Prodloužit o 30 dní.
- Filter chips: status, role, search.

**Step 2: Detail route `/organizace/hry/{gameId}/zpetna-vazba/{registrationId}`**

- Read-only answers (one section per question).
- "Pin do kroniky" toggle.
- "Poznámka organizátora" textarea (saves on blur).

**Step 3: Window override actions**

`OpenFeedbackAsync(gameId)` sets `Game.FeedbackOpensAtUtc = nowUtc`, leaves close untouched if already in future, otherwise `EndsAtUtc + 30d`.
`CloseFeedbackAsync(gameId)` sets `Game.FeedbackClosesAtUtc = nowUtc`.
`ExtendFeedbackAsync(gameId)` sets `Game.FeedbackClosesAtUtc = max(now, current) + 30d`.

**Step 4: Commit per sub-page**

```bash
git commit -m "feat(feedback): organizer dashboard + per-attendee detail + window override"
```

---

## Task 11: Excel export

**Files:**
- Create: `src/RegistraceOvcina.Web/Features/Feedback/FeedbackExportService.cs`
- Modify: `src/RegistraceOvcina.Web/Components/Pages/Organizer/FeedbackDashboard.razor` — add Export button.
- Test: `tests/RegistraceOvcina.Tests/Features/Feedback/FeedbackExportServiceTests.cs`

**Step 1: Test**

```csharp
[Fact]
public async Task Export_writes_two_sheets_with_question_columns()
{
    // Arrange seed: 2 player + 1 helper registration with answers
    var bytes = await sut.ExportAsync(gameId, default);

    using var workbook = new XLWorkbook(new MemoryStream(bytes));
    var kid = workbook.Worksheet("Děti");
    var adult = workbook.Worksheet("Dospělí");

    Assert.Equal("Jaký jsi měl zážitek?", kid.Cell(1, 5).Value.ToString());
    // ... etc
}
```

**Step 2-4: Implement using ClosedXML** mirroring `CharacterPrepExportService.cs` style (one row per Registration, headers from question.label).

**Step 5: Commit**

```bash
git commit -m "feat(feedback): Excel export (Děti + Dospělí sheets)"
```

---

## Task 12: Placeholder seeding for Game 1

**Files:**
- Modify: existing migration file (or new follow-up migration `SeedFeedbackQuestionsForGame1`)
- Create: `docs/feedback-placeholder-curation-2026-05-05.md` (working notes — reference, not committed source-of-truth)

**Step 1: Curation pass**

I (the agent) fetch `https://docs.google.com/document/d/1_Zkc-Ho8ZTYATQc6KNJTV6b0W62weRPTWjdsGfpKltc` and extract the kid answers. For each of the 7 kid questions, I curate **10 short, varied, anonymized** placeholder examples (no names, no specifics that identify the speaker). Surface the curated 10×7 to the user for review BEFORE encoding into JSON.

**Step 2: Encode into JSON**

```json
[
  {
    "key": "strongest_moment",
    "label": "Jaký jsi měl nejsilnější zážitek ze hry, negativní/pozitivní?",
    "helpText": "Klidně oba. Krátké poctivé věty stačí.",
    "placeholders": ["...", "...", "...", "...", "...", "...", "...", "...", "...", "..."]
  },
  ...
]
```

**Step 3: Migration data step**

```csharp
// In a follow-up empty migration:
migrationBuilder.Sql("""
    UPDATE "Games"
    SET "FeedbackKidQuestionsJson" = $$ <JSON> $$
    WHERE "Id" = 1
      AND "FeedbackKidQuestionsJson" IS NULL;
""");
```

Adult set seeds with the 7 question labels but `placeholders: []`.

**Step 4: Commit**

```bash
git commit -m "feat(feedback): seed Game 1 question schema + curated kid placeholders"
```

---

## Task 13: E2E tests

**Files:**
- Create: `tests/RegistraceOvcina.E2E/Feedback/FeedbackTokenFlowTests.cs`
- Create: `tests/RegistraceOvcina.E2E/Feedback/FeedbackScribeFlowTests.cs`
- Create: `tests/RegistraceOvcina.E2E/Feedback/FeedbackOrganizerFlowTests.cs`

**Step 1: `FillFeedbackViaTokenAsync`**

Spin up app + Postgres, seed Game + Registration with FeedbackToken, navigate to `/zpetna-vazba/{token}`, fill 7 memos, click Hotovo, refresh, assert Done badge somewhere.

**Step 2: `ScribeFeedbackForChildAsync`**

Log in as parent, open submission detail, click "Vyplnit" on kid attendee, fill, save, assert `LastEditedBy = HouseholdContact` via DB query.

**Step 3: `OrganizerInviteAndReminderFlowAsync`**

Log in as organizer, navigate dashboard, click Poslat pozvánky, assert mock email sender received the right shape (bundle + individuals), navigate token from logged email, fill, return to dashboard, assert status = Done.

**Step 4: Commit**

```bash
git commit -m "test(feedback): E2E coverage for token + scribe + organizer flows"
```

---

## Task 14: PR prep + version bump

**Files:**
- Modify: `src/RegistraceOvcina.Web/RegistraceOvcina.Web.csproj` — bump `<Version>` patch.

**Step 1: Bump patch version**

Read current version (e.g. `0.9.54`) → `0.9.55`.

**Step 2: Smoke test full happy path locally**

1. `dotnet build` clean.
2. `dotnet test` all green.
3. `dotnet run` and walk the form flow end-to-end (token + scribe + organizer).

**Step 3: Push branch**

```bash
git push -u origin feature/post-game-feedback
```

**Step 4: Open PR**

```bash
gh pr create --title "feat(feedback): post-game feedback form (kid + adult)" --body "$(cat <<'EOF'
## Summary
- Per-attendee post-game feedback form with kid and adult question sets defined as JSON on Game (extensible per game without schema changes)
- Public token link path + logged-in scribe path (parent fills for kids who can't type)
- Organizer dashboard: stats, invitations, 24h-throttled reminders, window open/close/extend, per-attendee detail with pin-to-chronicle, Excel export

## Test plan
- [ ] Unit tests pass (`dotnet test`)
- [ ] E2E tests pass (Playwright)
- [ ] Migration applies cleanly (`dotnet ef database update`) and no pending model changes
- [ ] Manual: token form fills end-to-end on mobile viewport
- [ ] Manual: parent logs in, scribes for kid, audit shows HouseholdContact
- [ ] Manual: organizer sends bundle email + individual email, links resolve
- [ ] Manual: window override (open/close/extend) takes effect immediately
- [ ] Excel export downloads and opens cleanly with Děti + Dospělí sheets

## Design
docs/plans/2026-05-05-feedback-form-design.md

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

**Step 5: Wait for CI, ASK USER before merging**

Per project guardrails: never merge to main without explicit user permission.

---

## Process notes

- **Run `dotnet build` after every task** — early type errors in C# rot fast.
- **Run `dotnet ef migrations has-pending-model-changes` after every model edit** — missing migrations are the #1 cause of prod crashes here.
- **Commit per sub-task, not per task** — keeps history reviewable. The plan suggests one commit per Task as a minimum; sub-task commits are encouraged.
- **`Random.Shared` is fine for placeholder rotation** — no security implications, cosmetic only.
- **Don't add ToString placeholders to git history** — work with curated final examples only.
