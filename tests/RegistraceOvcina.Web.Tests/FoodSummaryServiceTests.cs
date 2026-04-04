using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Features.Food;

namespace RegistraceOvcina.Web.Tests;

public sealed class FoodSummaryServiceTests
{
    [Fact]
    public async Task GetPageAsync_AggregatesSubmittedActiveFoodOrdersByDayAndOption()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        var dayOne = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var dayTwo = dayOne.AddDays(1);
        var registrantUser = new ApplicationUser
        {
            Id = "registrant-1",
            UserName = "registrant@example.cz",
            NormalizedUserName = "REGISTRANT@EXAMPLE.CZ",
            Email = "registrant@example.cz",
            NormalizedEmail = "REGISTRANT@EXAMPLE.CZ",
            EmailConfirmed = true,
            IsActive = true,
            CreatedAtUtc = dayOne.AddMonths(-1)
        };

        await using (var db = new ApplicationDbContext(options))
        {
            var game = new Game
            {
                Id = 100,
                Name = "Ovčina 2026",
                StartsAtUtc = dayOne.AddHours(9),
                EndsAtUtc = dayTwo.AddHours(17),
                RegistrationClosesAtUtc = dayOne.AddDays(-7),
                MealOrderingClosesAtUtc = dayOne.AddDays(-10),
                PaymentDueAtUtc = dayOne.AddDays(-5),
                BankAccount = "CZ6508000000192000145399",
                BankAccountName = "Ovčina z.s.",
                VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
                TargetPlayerCountTotal = 120,
                IsPublished = true,
                CreatedAtUtc = dayOne.AddMonths(-2),
                UpdatedAtUtc = dayOne.AddMonths(-2)
            };

            var soup = new MealOption { Id = 201, Game = game, Name = "Polévka", Price = 85m, IsActive = true };
            var lunch = new MealOption { Id = 202, Game = game, Name = "Oběd", Price = 120m, IsActive = true };
            var inactive = new MealOption { Id = 203, Game = game, Name = "Neaktivní", Price = 999m, IsActive = false };

            var submitted = new RegistrationSubmission
            {
                Id = 301,
                Game = game,
                RegistrantUserId = registrantUser.Id,
                PrimaryContactName = "Rodina",
                PrimaryEmail = "rodina@example.cz",
                PrimaryPhone = "+420777123456",
                Status = SubmissionStatus.Submitted,
                SubmittedAtUtc = dayOne.AddDays(-1),
                LastEditedAtUtc = dayOne.AddDays(-1),
                ExpectedTotalAmount = 0m
            };

            var draft = new RegistrationSubmission
            {
                Id = 302,
                Game = game,
                RegistrantUserId = registrantUser.Id,
                PrimaryContactName = "Rodina draft",
                PrimaryEmail = "draft@example.cz",
                PrimaryPhone = "+420777000000",
                Status = SubmissionStatus.Draft,
                LastEditedAtUtc = dayOne.AddDays(-1),
                ExpectedTotalAmount = 0m
            };

            var personOne = new Person { Id = 401, FirstName = "Anna", LastName = "Nováková", BirthYear = 2014, CreatedAtUtc = dayOne, UpdatedAtUtc = dayOne };
            var personTwo = new Person { Id = 402, FirstName = "Petr", LastName = "Novák", BirthYear = 2012, CreatedAtUtc = dayOne, UpdatedAtUtc = dayOne };
            var personThree = new Person { Id = 403, FirstName = "Eva", LastName = "Nováková", BirthYear = 2010, CreatedAtUtc = dayOne, UpdatedAtUtc = dayOne };
            var personFour = new Person { Id = 404, FirstName = "Lucie", LastName = "Draftová", BirthYear = 2015, CreatedAtUtc = dayOne, UpdatedAtUtc = dayOne };

            var activeRegistrationOne = new Registration
            {
                Id = 501,
                Submission = submitted,
                Person = personOne,
                AttendeeType = AttendeeType.Player,
                Status = RegistrationStatus.Active,
                CreatedAtUtc = dayOne,
                UpdatedAtUtc = dayOne
            };

            var activeRegistrationTwo = new Registration
            {
                Id = 502,
                Submission = submitted,
                Person = personTwo,
                AttendeeType = AttendeeType.Player,
                Status = RegistrationStatus.Active,
                CreatedAtUtc = dayOne,
                UpdatedAtUtc = dayOne
            };

            var cancelledRegistration = new Registration
            {
                Id = 503,
                Submission = submitted,
                Person = personThree,
                AttendeeType = AttendeeType.Player,
                Status = RegistrationStatus.Cancelled,
                CreatedAtUtc = dayOne,
                UpdatedAtUtc = dayOne
            };

            var draftRegistration = new Registration
            {
                Id = 504,
                Submission = draft,
                Person = personFour,
                AttendeeType = AttendeeType.Player,
                Status = RegistrationStatus.Active,
                CreatedAtUtc = dayOne,
                UpdatedAtUtc = dayOne
            };

            db.Users.Add(registrantUser);
            db.AddRange(game, soup, lunch, inactive, submitted, draft, personOne, personTwo, personThree, personFour);
            db.AddRange(activeRegistrationOne, activeRegistrationTwo, cancelledRegistration, draftRegistration);
            db.FoodOrders.AddRange(
                new FoodOrder { Registration = activeRegistrationOne, MealOption = soup, MealDayUtc = dayOne, Price = soup.Price },
                new FoodOrder { Registration = activeRegistrationOne, MealOption = lunch, MealDayUtc = dayOne, Price = lunch.Price },
                new FoodOrder { Registration = activeRegistrationTwo, MealOption = soup, MealDayUtc = dayOne, Price = soup.Price },
                new FoodOrder { Registration = activeRegistrationTwo, MealOption = lunch, MealDayUtc = dayTwo, Price = lunch.Price },
                new FoodOrder { Registration = cancelledRegistration, MealOption = soup, MealDayUtc = dayOne, Price = soup.Price },
                new FoodOrder { Registration = draftRegistration, MealOption = lunch, MealDayUtc = dayOne, Price = lunch.Price },
                new FoodOrder { Registration = activeRegistrationOne, MealOption = inactive, MealDayUtc = dayTwo, Price = inactive.Price });

            await db.SaveChangesAsync();
        }

        var factory = new TestDbContextFactory(options);
        var service = new FoodSummaryService(factory);

        var summary = await service.GetPageAsync(100);

        Assert.NotNull(summary.SelectedGame);
        Assert.Equal(100, summary.SelectedGame!.Id);
        Assert.Equal(4, summary.TotalSelections);
        Assert.Equal(2, summary.RegistrationsWithOrders);

        var overallTotals = summary.OverallTotals.ToDictionary(x => x.MealOptionId);
        Assert.Equal(2, overallTotals[201].Count);
        Assert.Equal(2, overallTotals[202].Count);
        Assert.DoesNotContain(summary.OverallTotals, x => x.MealOptionId == 203);

        var firstDay = Assert.Single(summary.Days, x => x.MealDayUtc == dayOne);
        Assert.Equal(3, firstDay.TotalSelections);
        Assert.Equal(2, firstDay.Options.Single(x => x.MealOptionId == 201).Count);
        Assert.Equal(1, firstDay.Options.Single(x => x.MealOptionId == 202).Count);

        var secondDay = Assert.Single(summary.Days, x => x.MealDayUtc == dayTwo);
        Assert.Equal(1, secondDay.TotalSelections);
        Assert.Equal(0, secondDay.Options.Single(x => x.MealOptionId == 201).Count);
        Assert.Equal(1, secondDay.Options.Single(x => x.MealOptionId == 202).Count);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }
}
