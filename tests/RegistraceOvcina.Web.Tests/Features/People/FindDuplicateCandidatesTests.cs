using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.People;

namespace RegistraceOvcina.Web.Tests.Features.People;

public sealed class FindDuplicateCandidatesTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task No_duplicates_returns_empty()
    {
        var options = CreateOptions();
        await SeedAsync(options,
            CreatePerson(1, "Jan", "Novák", 1980, null, null),
            CreatePerson(2, "Petr", "Svoboda", 1985, null, null));

        var service = new PeopleReviewService(new TestDbContextFactory(options), new FixedTimeProvider());

        var result = await service.FindDuplicateCandidatesAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task ExactNameBirthYearWithin1_returns_candidate_with_high_score()
    {
        var options = CreateOptions();
        await SeedAsync(options,
            CreatePerson(1, "Jan", "Novák", 1980, null, null),
            CreatePerson(2, "Jan", "Novák", 1981, null, null));

        var service = new PeopleReviewService(new TestDbContextFactory(options), new FixedTimeProvider());

        var result = await service.FindDuplicateCandidatesAsync();

        var pair = Assert.Single(result);
        Assert.Equal(DuplicateMatchReason.ExactNameBirthYearWithin1, pair.Reason);
        Assert.Equal(95, pair.ConfidenceScore);
    }

    [Fact]
    public async Task ExactNameBirthYearMoreThan1_not_returned()
    {
        var options = CreateOptions();
        await SeedAsync(options,
            CreatePerson(1, "Jan", "Novák", 1980, null, null),
            CreatePerson(2, "Jan", "Novák", 1983, null, null));

        var service = new PeopleReviewService(new TestDbContextFactory(options), new FixedTimeProvider());

        var result = await service.FindDuplicateCandidatesAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task DiminutiveMatch_Jan_Honza_returns_candidate()
    {
        var options = CreateOptions();
        await SeedAsync(options,
            CreatePerson(1, "Jan", "Novák", 1980, null, null),
            CreatePerson(2, "Honza", "Novák", 1981, null, null));

        var service = new PeopleReviewService(new TestDbContextFactory(options), new FixedTimeProvider());

        var result = await service.FindDuplicateCandidatesAsync();

        var pair = Assert.Single(result);
        Assert.Equal(DuplicateMatchReason.DiminutiveNameBirthYearWithin1, pair.Reason);
        Assert.Equal(90, pair.ConfidenceScore);
    }

    [Fact]
    public async Task FuzzyFirstName_returns_candidate_with_lower_score()
    {
        var options = CreateOptions();
        await SeedAsync(options,
            // Jakub vs Jakob: Levenshtein = 1, not in diminutives dictionary.
            CreatePerson(1, "Jakub", "Dvořák", 2005, null, null),
            CreatePerson(2, "Jakob", "Dvořák", 2006, null, null));

        var service = new PeopleReviewService(new TestDbContextFactory(options), new FixedTimeProvider());

        var result = await service.FindDuplicateCandidatesAsync();

        var pair = Assert.Single(result);
        Assert.Equal(DuplicateMatchReason.FuzzyNameBirthYearWithin1, pair.Reason);
        Assert.Equal(85, pair.ConfidenceScore);
    }

    [Fact]
    public async Task SharedPhone_returns_candidate_regardless_of_name()
    {
        var options = CreateOptions();
        await SeedAsync(options,
            CreatePerson(1, "Jana", "Nováková", 1980, null, "777 111 222"),
            CreatePerson(2, "Eva", "Svobodová", 1985, null, "777111222"));

        var service = new PeopleReviewService(new TestDbContextFactory(options), new FixedTimeProvider());

        var result = await service.FindDuplicateCandidatesAsync();

        var pair = Assert.Single(result);
        Assert.Equal(DuplicateMatchReason.SharedPhoneOrEmail, pair.Reason);
        Assert.Equal(88, pair.ConfidenceScore);
    }

    [Fact]
    public async Task SoftDeletedPerson_excluded()
    {
        var options = CreateOptions();
        var alive = CreatePerson(1, "Jan", "Novák", 1980, null, null);
        var deleted = CreatePerson(2, "Jan", "Novák", 1981, null, null);
        deleted.IsDeleted = true;
        await SeedAsync(options, alive, deleted);

        var service = new PeopleReviewService(new TestDbContextFactory(options), new FixedTimeProvider());

        var result = await service.FindDuplicateCandidatesAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task Duplicate_pair_not_returned_twice()
    {
        var options = CreateOptions();
        // Same pair could be matched by exact-name-and-year rule AND by shared phone — must be one pair.
        await SeedAsync(options,
            CreatePerson(1, "Jan", "Novák", 1980, null, "777111222"),
            CreatePerson(2, "Jan", "Novák", 1981, null, "777111222"));

        var service = new PeopleReviewService(new TestDbContextFactory(options), new FixedTimeProvider());

        var result = await service.FindDuplicateCandidatesAsync();

        var pair = Assert.Single(result);
        // Highest scoring rule wins.
        Assert.Equal(DuplicateMatchReason.ExactNameBirthYearWithin1, pair.Reason);
        Assert.Equal(95, pair.ConfidenceScore);
    }

    // ------------------------------------------------------------------------ helpers

    private static DbContextOptions<ApplicationDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

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

    private static async Task SeedAsync(DbContextOptions<ApplicationDbContext> options, params Person[] people)
    {
        await using var db = new ApplicationDbContext(options);
        db.People.AddRange(people);
        await db.SaveChangesAsync();
    }

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
