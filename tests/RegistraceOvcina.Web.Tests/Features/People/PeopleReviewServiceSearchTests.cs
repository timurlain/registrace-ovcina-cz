using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.People;
using Testcontainers.PostgreSql;

namespace RegistraceOvcina.Web.Tests.Features.People;

/// <summary>
/// Regression tests for v0.9.21: case-insensitive search on /organizace/osoby.
///
/// The bug was that EF Core translated <c>.Contains(trimmed)</c> to Postgres
/// <c>LIKE '%...%'</c>, which is case-sensitive. Searching "fila" returned zero
/// rows despite "Fila" existing. The fix is <c>EF.Functions.ILike</c>.
///
/// These tests run against a real PostgreSQL container so the ILIKE translation
/// actually happens — that is the only place where the fix has observable behavior.
/// </summary>
public sealed class PeopleReviewServiceSearchTests : IAsyncLifetime
{
    private static readonly DateTime FixedUtc = new(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase($"registrace_ovcina_people_search_{Guid.NewGuid():N}")
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
    public async Task GetListAsync_no_query_returns_all_active_people()
    {
        await SeedAsync(
            CreatePerson(1, "Jakub", "Fila", 2010, "jakub@example.cz", "608123456"),
            CreatePerson(2, "Martin", "Fila", 2012, null, null),
            CreatePerson(3, "Anna", "Nováková", 2011, null, null));

        var service = CreateService();

        var result = await service.GetListAsync(query: null);

        Assert.Equal(3, result.People.Count);
    }

    [Fact]
    public async Task GetListAsync_lowercase_query_matches_capitalized_names()
    {
        // This is the exact symptom the user reported: "fila" finds nothing
        // despite "Fila" existing. Before the fix, LIKE was case-sensitive.
        await SeedAsync(
            CreatePerson(1, "Jakub", "Fila", 2010, null, null),
            CreatePerson(2, "Martin", "Fila", 2012, null, null),
            CreatePerson(3, "Anna", "Nováková", 2011, null, null));

        var service = CreateService();

        var result = await service.GetListAsync(query: "fila");

        Assert.Equal(2, result.People.Count);
        Assert.Contains(result.People, p => p.FullName == "Jakub Fila");
        Assert.Contains(result.People, p => p.FullName == "Martin Fila");
    }

    [Fact]
    public async Task GetListAsync_uppercase_query_matches_lowercase_email()
    {
        await SeedAsync(
            CreatePerson(1, "Tomáš", "Kopecký", 2011, "test@x.cz", null));

        var service = CreateService();

        var result = await service.GetListAsync(query: "TEST");

        Assert.Single(result.People);
        Assert.Equal("test@x.cz", result.People[0].Email);
    }

    [Fact]
    public async Task GetListAsync_phone_partial_match_works_case_insensitive()
    {
        await SeedAsync(
            CreatePerson(1, "Jana", "Nová", 2013, null, "608123456"));

        var service = CreateService();

        var result = await service.GetListAsync(query: "608123");

        Assert.Single(result.People);
        Assert.Equal("608123456", result.People[0].Phone);
    }

    [Fact]
    public async Task GetListAsync_trims_whitespace_on_query()
    {
        await SeedAsync(
            CreatePerson(1, "Jakub", "Fila", 2010, null, null),
            CreatePerson(2, "Anna", "Nováková", 2011, null, null));

        var service = CreateService();

        var trimmed = await service.GetListAsync(query: "fila");
        var padded = await service.GetListAsync(query: "  fila  ");

        Assert.Equal(trimmed.People.Count, padded.People.Count);
        Assert.Equal("fila", padded.Query);
    }

    [Fact]
    public async Task GetListAsync_no_match_returns_empty()
    {
        await SeedAsync(
            CreatePerson(1, "Jakub", "Fila", 2010, "jakub@example.cz", "608123456"));

        var service = CreateService();

        var result = await service.GetListAsync(query: "nonexistent");

        Assert.Empty(result.People);
    }

    [Fact]
    public async Task GetListAsync_special_like_chars_treated_literally()
    {
        // "%" and "_" are LIKE wildcards in Postgres. Without escaping, the
        // user's input would match things it shouldn't. EscapeLikePattern fixes this.
        await SeedAsync(
            CreatePerson(1, "Foo%Bar", "Test", 2010, null, null),
            CreatePerson(2, "FooXBar", "Test", 2011, null, null),
            CreatePerson(3, "Foo_Bar", "Test", 2012, null, null));

        var service = CreateService();

        // "%" should match only the row that literally contains "%", not the "X" row.
        var percentResult = await service.GetListAsync(query: "%");
        Assert.Single(percentResult.People);
        Assert.Equal("Foo%Bar Test", percentResult.People[0].FullName);

        // "_" should match only the row that literally contains "_", not the "X" row.
        var underscoreResult = await service.GetListAsync(query: "_");
        Assert.Single(underscoreResult.People);
        Assert.Equal("Foo_Bar Test", underscoreResult.People[0].FullName);
    }

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

/// <summary>
/// Pure unit tests for <see cref="PeopleReviewService.EscapeLikePattern"/>.
/// No DB dependency — exercises the escape helper directly.
/// </summary>
public sealed class EscapeLikePatternTests
{
    [Fact]
    public void plain_text_is_unchanged()
    {
        Assert.Equal("fila", PeopleReviewService.EscapeLikePattern("fila"));
    }

    [Fact]
    public void percent_is_escaped()
    {
        Assert.Equal("\\%", PeopleReviewService.EscapeLikePattern("%"));
    }

    [Fact]
    public void underscore_is_escaped()
    {
        Assert.Equal("\\_", PeopleReviewService.EscapeLikePattern("_"));
    }

    [Fact]
    public void backslash_is_escaped_before_wildcards()
    {
        // "\%" in input becomes "\\\%" — the raw backslash is escaped to "\\",
        // then the "%" is escaped to "\%". Order matters.
        Assert.Equal("\\\\\\%", PeopleReviewService.EscapeLikePattern("\\%"));
    }
}
