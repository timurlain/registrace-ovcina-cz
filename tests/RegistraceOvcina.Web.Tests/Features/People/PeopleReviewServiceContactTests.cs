using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.People;
using Testcontainers.PostgreSql;

namespace RegistraceOvcina.Web.Tests.Features.People;

/// <summary>
/// Integration tests for v0.9.29: organizer-editable contact info on PersonDetail.
///
/// Covers <see cref="PeopleReviewService.UpdateContactAsync"/>: normalization, the
/// partial unique-email collision check, NoChange shortcut, soft-deleted lookup,
/// and AuditLog emission. Runs against real Postgres so the partial unique index
/// ("Email" IS NOT NULL AND "Email" != '') behaves the same way it does in prod.
/// </summary>
public sealed class PeopleReviewServiceContactTests : IAsyncLifetime
{
    private static readonly DateTime FixedUtc = new(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc);
    private const string ActorUserId = "test-organizer";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase($"registrace_ovcina_people_contact_{Guid.NewGuid():N}")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private DbContextOptions<ApplicationDbContext> _options = default!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var db = new ApplicationDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task UpdateContactAsync_updates_email_and_phone()
    {
        await SeedAsync(CreatePerson(1, "Jakub", "Fila", 2010, null, null));

        var service = CreateService();

        var result = await service.UpdateContactAsync(1, "  Jakub@Example.CZ ", "+420 608 123 456", ActorUserId);

        Assert.Equal(UpdateContactOutcome.Updated, result.Outcome);
        Assert.Equal("jakub@example.cz", result.NormalizedEmail);
        Assert.Equal("420608123456", result.NormalizedPhone);

        await using var db = new ApplicationDbContext(_options);
        var person = await db.People.SingleAsync();
        Assert.Equal("jakub@example.cz", person.Email);
        Assert.Equal("420608123456", person.Phone);
        Assert.Equal(FixedUtc, person.UpdatedAtUtc);
    }

    [Fact]
    public async Task UpdateContactAsync_email_conflict_returns_error_and_does_not_save()
    {
        await SeedAsync(
            CreatePerson(1, "Jakub", "Fila", 2010, null, "111"),
            CreatePerson(2, "Martin", "Fila", 2012, "taken@example.cz", "222"));

        var service = CreateService();

        var result = await service.UpdateContactAsync(1, "taken@example.cz", "999", ActorUserId);

        Assert.Equal(UpdateContactOutcome.EmailAlreadyUsedByOtherPerson, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));

        await using var db = new ApplicationDbContext(_options);
        var person = await db.People.SingleAsync(x => x.Id == 1);
        Assert.Null(person.Email);
        Assert.Equal("111", person.Phone);

        Assert.Equal(0, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task UpdateContactAsync_null_input_clears_fields()
    {
        await SeedAsync(CreatePerson(1, "Jakub", "Fila", 2010, "old@example.cz", "123456789"));

        var service = CreateService();

        var result = await service.UpdateContactAsync(1, "", "   ", ActorUserId);

        Assert.Equal(UpdateContactOutcome.Updated, result.Outcome);
        Assert.Null(result.NormalizedEmail);
        Assert.Null(result.NormalizedPhone);

        await using var db = new ApplicationDbContext(_options);
        var person = await db.People.SingleAsync();
        Assert.Null(person.Email);
        Assert.Null(person.Phone);
    }

    [Fact]
    public async Task UpdateContactAsync_identical_input_returns_NoChange()
    {
        await SeedAsync(CreatePerson(1, "Jakub", "Fila", 2010, "same@example.cz", "608123456"));

        var service = CreateService();

        // Input normalizes to the exact stored values — no work to do, no audit row.
        var result = await service.UpdateContactAsync(1, "same@example.cz", "608123456", ActorUserId);

        Assert.Equal(UpdateContactOutcome.NoChange, result.Outcome);

        await using var db = new ApplicationDbContext(_options);
        Assert.Equal(0, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task UpdateContactAsync_person_not_found_returns_NotFound()
    {
        var service = CreateService();

        var result = await service.UpdateContactAsync(9999, "x@y.cz", "608000000", ActorUserId);

        Assert.Equal(UpdateContactOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task UpdateContactAsync_soft_deleted_person_returns_NotFound()
    {
        // Soft-deleted Persons are hidden by the global query filter. The organizer
        // should not be able to edit their contact by guessing the id.
        await using (var seedDb = new ApplicationDbContext(_options))
        {
            seedDb.People.Add(new Person
            {
                Id = 1,
                FirstName = "Deleted",
                LastName = "Person",
                BirthYear = 2010,
                IsDeleted = true,
                CreatedAtUtc = FixedUtc,
                UpdatedAtUtc = FixedUtc
            });
            await seedDb.SaveChangesAsync();
        }

        var service = CreateService();

        var result = await service.UpdateContactAsync(1, "x@y.cz", null, ActorUserId);

        Assert.Equal(UpdateContactOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task UpdateContactAsync_writes_AuditLog()
    {
        await SeedAsync(CreatePerson(1, "Jakub", "Fila", 2010, "old@example.cz", "111"));

        var service = CreateService();

        var result = await service.UpdateContactAsync(1, "new@example.cz", "222", ActorUserId);
        Assert.Equal(UpdateContactOutcome.Updated, result.Outcome);

        await using var db = new ApplicationDbContext(_options);
        var audit = await db.AuditLogs.SingleAsync();

        Assert.Equal("Person", audit.EntityType);
        Assert.Equal("1", audit.EntityId);
        Assert.Equal("UpdateContact", audit.Action);
        Assert.Equal(ActorUserId, audit.ActorUserId);
        Assert.Equal(FixedUtc, audit.CreatedAtUtc);
        Assert.NotNull(audit.DetailsJson);
        Assert.Contains("old@example.cz", audit.DetailsJson);
        Assert.Contains("new@example.cz", audit.DetailsJson);
        Assert.Contains("Email", audit.DetailsJson);
        Assert.Contains("Phone", audit.DetailsJson);
    }

    [Fact]
    public async Task UpdateContactAsync_same_email_different_case_is_not_a_conflict_with_self()
    {
        // A Person updating their own email to the same value with different casing
        // should not trip the uniqueness check.
        await SeedAsync(CreatePerson(1, "Jakub", "Fila", 2010, "same@example.cz", "111"));

        var service = CreateService();

        var result = await service.UpdateContactAsync(1, "SAME@EXAMPLE.CZ", "111", ActorUserId);

        // Normalization lowercases → matches stored value → NoChange (no audit, no conflict).
        Assert.Equal(UpdateContactOutcome.NoChange, result.Outcome);
    }

    [Fact]
    public async Task UpdateContactAsync_case_insensitive_email_collision_detected()
    {
        // Copilot followup (v0.9.30): legacy imports have stored emails in mixed case.
        // Person A's email is "TEST@X.CZ"; Person B tries to claim "test@x.cz".
        // A case-sensitive Postgres `=` would miss this and surface a 500 at SaveChanges
        // via the partial unique index. Collision MUST be detected at the app layer.
        //
        // Use direct SQL to seed A's email in uppercase — the normal UpdateContactAsync
        // path lowercases on write, so we bypass it to reproduce the legacy-data shape.
        await SeedAsync(
            CreatePerson(1, "Anna", "Import", 2010, null, "111"),
            CreatePerson(2, "Bedřich", "Novák", 2011, null, "222"));

        await using (var seedDb = new ApplicationDbContext(_options))
        {
            await seedDb.Database.ExecuteSqlRawAsync(
                """UPDATE "People" SET "Email" = 'TEST@X.CZ' WHERE "Id" = 1""");
        }

        var service = CreateService();

        var result = await service.UpdateContactAsync(2, "test@x.cz", "222", ActorUserId);

        Assert.Equal(UpdateContactOutcome.EmailAlreadyUsedByOtherPerson, result.Outcome);

        // Person B's email should NOT have been written.
        await using var db = new ApplicationDbContext(_options);
        var b = await db.People.SingleAsync(x => x.Id == 2);
        Assert.Null(b.Email);
    }

    // NOTE (v0.9.30 — Copilot followup #3):
    // A test for the DbUpdateException race translation was considered but skipped
    // per the plan's explicit allowance ("or skip this test and just rely on the
    // hand-rolled logic"). Reasons:
    //   1. Reproducing the real race requires a SaveChanges interceptor that holds
    //      the app-level check's snapshot, which is brittle and slow.
    //   2. Npgsql's `PostgresException.ConstraintName` is read-only with no public
    //      constructor that sets it, so a hand-built fake can't exercise the
    //      `IsUniqueEmailViolation` constraint-name check.
    //   3. Manual QA on staging exercises the end-to-end path.
    // The hand-rolled predicate in PeopleReviewService.IsUniqueEmailViolation is
    // tight: SqlState == 23505 AND ConstraintName contains "Email".

    private PeopleReviewService CreateService() =>
        new(new TestDbContextFactory(_options), new FixedTimeProvider());

    private async Task SeedAsync(params Person[] people)
    {
        await using var db = new ApplicationDbContext(_options);
        db.People.AddRange(people);
        await db.SaveChangesAsync();
    }

    private static Person CreatePerson(
        int id,
        string firstName,
        string lastName,
        int birthYear,
        string? email,
        string? phone) =>
        new()
        {
            Id = id,
            FirstName = firstName,
            LastName = lastName,
            BirthYear = birthYear,
            Email = email,
            Phone = phone,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        };

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ApplicationDbContext(options));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now = new(FixedUtc);

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
