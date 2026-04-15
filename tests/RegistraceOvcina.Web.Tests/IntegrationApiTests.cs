using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Integration;

namespace RegistraceOvcina.Web.Tests;

/// <summary>
/// Unit tests for the API key filter and DTO shapes.
/// These tests do not require a running database — they exercise the filter logic directly.
/// </summary>
public sealed class IntegrationApiOptionsTests
{
    [Fact]
    public void SectionName_IsExpectedValue()
    {
        Assert.Equal("IntegrationApi", IntegrationApiOptions.SectionName);
    }

    [Fact]
    public void DefaultApiKey_IsEmpty()
    {
        var opts = new IntegrationApiOptions();
        Assert.Equal("", opts.ApiKey);
    }
}

public sealed class ApiKeyEndpointFilterTests
{
    private static ApiKeyEndpointFilter CreateFilter(string configuredKey)
    {
        var options = Options.Create(new IntegrationApiOptions { ApiKey = configuredKey });
        return new ApiKeyEndpointFilter(options);
    }

    private static DefaultHttpContext CreateHttpContext(string? headerValue)
    {
        var ctx = new DefaultHttpContext();
        if (headerValue is not null)
            ctx.Request.Headers["X-Api-Key"] = headerValue;
        return ctx;
    }

    [Fact]
    public async Task MissingKey_Returns401()
    {
        var filter = CreateFilter("secret-key");
        var httpCtx = CreateHttpContext(null);
        var efCtx = new FakeEndpointFilterContext(httpCtx);

        var result = await filter.InvokeAsync(efCtx, _ => ValueTask.FromResult<object?>(Results.Ok()));

        var httpResult = Assert.IsAssignableFrom<IResult>(result);
        Assert.IsType<UnauthorizedHttpResult>(httpResult);
    }

    [Fact]
    public async Task WrongKey_Returns401()
    {
        var filter = CreateFilter("secret-key");
        var httpCtx = CreateHttpContext("wrong-key");
        var efCtx = new FakeEndpointFilterContext(httpCtx);

        var result = await filter.InvokeAsync(efCtx, _ => ValueTask.FromResult<object?>(Results.Ok()));

        var httpResult = Assert.IsAssignableFrom<IResult>(result);
        Assert.IsType<UnauthorizedHttpResult>(httpResult);
    }

    [Fact]
    public async Task CorrectKey_PassesThrough()
    {
        var filter = CreateFilter("secret-key");
        var httpCtx = CreateHttpContext("secret-key");
        var efCtx = new FakeEndpointFilterContext(httpCtx);

        var sentinel = Results.Ok("passed");
        var result = await filter.InvokeAsync(efCtx, _ => ValueTask.FromResult<object?>(sentinel));

        Assert.Same(sentinel, result);
    }

    [Fact]
    public async Task EmptyConfiguredKey_Returns503()
    {
        var filter = CreateFilter("");
        var httpCtx = CreateHttpContext("any-key");
        var efCtx = new FakeEndpointFilterContext(httpCtx);

        var result = await filter.InvokeAsync(efCtx, _ => ValueTask.FromResult<object?>(Results.Ok()));

        var httpResult = Assert.IsAssignableFrom<IResult>(result);
        // Should be a ProblemHttpResult (503)
        Assert.IsType<ProblemHttpResult>(httpResult);
    }

    [Fact]
    public async Task KeyIsCaseSensitive()
    {
        var filter = CreateFilter("Secret-Key");
        var httpCtx = CreateHttpContext("secret-key");
        var efCtx = new FakeEndpointFilterContext(httpCtx);

        var result = await filter.InvokeAsync(efCtx, _ => ValueTask.FromResult<object?>(Results.Ok()));

        var httpResult = Assert.IsAssignableFrom<IResult>(result);
        Assert.IsType<UnauthorizedHttpResult>(httpResult);
    }
}

public sealed class IntegrationApiDtoTests
{
    [Fact]
    public void GameDto_RecordEquality()
    {
        var now = DateTime.UtcNow;
        var a = new GameDto(1, "Ovčina 2026", null, now, now, now, 100, true);
        var b = new GameDto(1, "Ovčina 2026", null, now, now, now, 100, true);
        Assert.Equal(a, b);
    }

    [Fact]
    public void RegistrationDto_RecordEquality()
    {
        var a = new RegistrationDto(10, 5, "Jan", "Novák", 2000, "Player", "Frodo", "Active");
        var b = new RegistrationDto(10, 5, "Jan", "Novák", 2000, "Player", "Frodo", "Active");
        Assert.Equal(a, b);
    }

    [Fact]
    public void PresenceCheckDto_IsRegistered_True()
    {
        var dto = new PresenceCheckDto(true);
        Assert.True(dto.IsRegistered);
    }

    [Fact]
    public void PresenceCheckDto_IsRegistered_False()
    {
        var dto = new PresenceCheckDto(false);
        Assert.False(dto.IsRegistered);
    }
}

public sealed class AdultsEndpointTests
{
    private static readonly DateTime FixedUtc = new(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task LoadAdultsAsync_ReturnsDistinctAdultsWithEmailAndRoles()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var game = CreateGame(1);
        var otherGame = CreateGame(2);

        // Registrants (parents/organizers) — all adults
        var jana = CreatePerson(10, "Jana", "Nováková", 1987, "jana@example.cz");
        var petr = CreatePerson(11, "Petr", "Dvořák", 1980, "petr@example.cz");
        var eva = CreatePerson(12, "Eva", "Bílá", 1991, null);
        // Kid — must be filtered out
        var kid = CreatePerson(13, "Kuba", "Nováková", 2016, null);
        // Adult in a different game — must not leak in
        var outsider = CreatePerson(14, "Mirek", "Cizí", 1975, "mirek@example.cz");

        var janaUser = CreateUser("u-jana", "jana@example.cz", personId: jana.Id);
        var petrUser = CreateUser("u-petr", "petr@example.cz", personId: petr.Id);
        var evaUser = CreateUser("u-eva", "eva@example.cz", personId: eva.Id);

        var submission1 = CreateSubmission(100, game.Id, janaUser.Id);
        var submission2 = CreateSubmission(101, game.Id, petrUser.Id);
        var cancelledSubmission = CreateSubmission(102, game.Id, evaUser.Id);
        cancelledSubmission.Status = SubmissionStatus.Draft; // must be ignored — not Submitted
        var otherGameSubmission = CreateSubmission(103, otherGame.Id, janaUser.Id);

        await using (var db = new ApplicationDbContext(options))
        {
            db.Games.AddRange(game, otherGame);
            db.People.AddRange(jana, petr, eva, kid, outsider);
            db.Users.AddRange(janaUser, petrUser, evaUser);
            db.RegistrationSubmissions.AddRange(submission1, submission2, cancelledSubmission, otherGameSubmission);

            // game 1: Jana (adult) + Kuba (kid) in submission1 — kid must be filtered out
            db.Registrations.Add(new Registration
            {
                Id = 1, SubmissionId = submission1.Id, PersonId = jana.Id,
                AttendeeType = AttendeeType.Adult, Status = RegistrationStatus.Active,
                CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
            });
            db.Registrations.Add(new Registration
            {
                Id = 2, SubmissionId = submission1.Id, PersonId = kid.Id,
                AttendeeType = AttendeeType.Player, Status = RegistrationStatus.Active,
                CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
            });
            // game 1: Petr (adult) in submission2, but also a CancelledRegistration that must not show up
            db.Registrations.Add(new Registration
            {
                Id = 3, SubmissionId = submission2.Id, PersonId = petr.Id,
                AttendeeType = AttendeeType.Adult, Status = RegistrationStatus.Active,
                CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
            });
            // game 1: Eva (adult) but her submission is in Draft, must be excluded
            db.Registrations.Add(new Registration
            {
                Id = 4, SubmissionId = cancelledSubmission.Id, PersonId = eva.Id,
                AttendeeType = AttendeeType.Adult, Status = RegistrationStatus.Active,
                CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
            });
            // game 2: Mirek — different game, must not leak in
            db.Registrations.Add(new Registration
            {
                Id = 5, SubmissionId = otherGameSubmission.Id, PersonId = outsider.Id,
                AttendeeType = AttendeeType.Adult, Status = RegistrationStatus.Active,
                CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
            });
            // Jana also appears on a second submission in the same game — must collapse to one row
            db.Registrations.Add(new Registration
            {
                Id = 6, SubmissionId = submission2.Id, PersonId = jana.Id,
                AttendeeType = AttendeeType.Adult, Status = RegistrationStatus.Active,
                CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
            });

            // Game roles: Jana is organizer+npc, Petr is helper, Eva has none assigned.
            db.GameRoles.AddRange(
                new GameRole { Id = 1, GameId = game.Id, UserId = janaUser.Id, RoleName = "organizer", AssignedAtUtc = FixedUtc },
                new GameRole { Id = 2, GameId = game.Id, UserId = janaUser.Id, RoleName = "npc", AssignedAtUtc = FixedUtc },
                new GameRole { Id = 3, GameId = game.Id, UserId = petrUser.Id, RoleName = "helper", AssignedAtUtc = FixedUtc },
                // Role on a different game — must not leak in
                new GameRole { Id = 4, GameId = otherGame.Id, UserId = janaUser.Id, RoleName = "organizer", AssignedAtUtc = FixedUtc });

            await db.SaveChangesAsync();
        }

        await using var queryDb = new ApplicationDbContext(options);
        var adults = await IntegrationApiEndpoints.LoadAdultsAsync(queryDb, game.Id, CancellationToken.None);

        Assert.Equal(2, adults.Count); // Jana + Petr, kid and cancelled Eva excluded
        Assert.DoesNotContain(adults, a => a.PersonId == kid.Id);
        Assert.DoesNotContain(adults, a => a.PersonId == eva.Id);
        Assert.DoesNotContain(adults, a => a.PersonId == outsider.Id);

        var janaDto = adults.Single(a => a.PersonId == jana.Id);
        Assert.Equal("Jana", janaDto.FirstName);
        Assert.Equal("Nováková", janaDto.LastName);
        Assert.Equal(1987, janaDto.BirthYear);
        Assert.Equal("jana@example.cz", janaDto.Email);
        Assert.Equal(["npc", "organizer"], janaDto.Roles); // sorted, and no otherGame leak

        var petrDto = adults.Single(a => a.PersonId == petr.Id);
        Assert.Equal("petr@example.cz", petrDto.Email);
        Assert.Equal(["helper"], petrDto.Roles);
    }

    [Fact]
    public async Task LoadAdultsAsync_ReturnsEmptyWhenNoAdults()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var game = CreateGame(1);
        var kid = CreatePerson(20, "Maruška", "Malá", 2017, null);
        var kidUser = CreateUser("u-parent", "parent@example.cz");
        var submission = CreateSubmission(200, game.Id, kidUser.Id);

        await using (var db = new ApplicationDbContext(options))
        {
            db.Games.Add(game);
            db.People.Add(kid);
            db.Users.Add(kidUser);
            db.RegistrationSubmissions.Add(submission);
            db.Registrations.Add(new Registration
            {
                Id = 1, SubmissionId = submission.Id, PersonId = kid.Id,
                AttendeeType = AttendeeType.Player, Status = RegistrationStatus.Active,
                CreatedAtUtc = FixedUtc, UpdatedAtUtc = FixedUtc
            });
            await db.SaveChangesAsync();
        }

        await using var queryDb = new ApplicationDbContext(options);
        var adults = await IntegrationApiEndpoints.LoadAdultsAsync(queryDb, game.Id, CancellationToken.None);

        Assert.Empty(adults);
    }

    private static Person CreatePerson(int id, string firstName, string lastName, int birthYear, string? email) =>
        new()
        {
            Id = id,
            FirstName = firstName,
            LastName = lastName,
            BirthYear = birthYear,
            Email = email,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc
        };

    private static Game CreateGame(int id) =>
        new()
        {
            Id = id,
            Name = $"Ovčina {id}",
            StartsAtUtc = FixedUtc.AddDays(30),
            EndsAtUtc = FixedUtc.AddDays(31),
            RegistrationClosesAtUtc = FixedUtc.AddDays(15),
            MealOrderingClosesAtUtc = FixedUtc.AddDays(10),
            PaymentDueAtUtc = FixedUtc.AddDays(20),
            PlayerBasePrice = 1200,
            AdultHelperBasePrice = 800,
            BankAccount = "123456789/0100",
            BankAccountName = "Ovčina",
            VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
            CreatedAtUtc = FixedUtc,
            UpdatedAtUtc = FixedUtc,
            IsPublished = true
        };

    private static RegistrationSubmission CreateSubmission(int id, int gameId, string registrantUserId) =>
        new()
        {
            Id = id,
            GameId = gameId,
            RegistrantUserId = registrantUserId,
            PrimaryContactName = "Test",
            PrimaryEmail = "kontakt@example.cz",
            PrimaryPhone = "777111222",
            Status = SubmissionStatus.Submitted,
            SubmittedAtUtc = FixedUtc,
            LastEditedAtUtc = FixedUtc,
            ExpectedTotalAmount = 1200
        };

    private static ApplicationUser CreateUser(string id, string email, int? personId = null) =>
        new()
        {
            Id = id,
            DisplayName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            IsActive = true,
            PersonId = personId,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = FixedUtc
        };
}

// Minimal test double for EndpointFilterInvocationContext
internal sealed class FakeEndpointFilterContext(HttpContext httpContext) : EndpointFilterInvocationContext
{
    public override HttpContext HttpContext => httpContext;
    public override IList<object?> Arguments => [];
    public override T GetArgument<T>(int index) => throw new NotSupportedException();
}
