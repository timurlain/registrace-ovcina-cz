using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Food;

public sealed class FoodSummaryService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    private static readonly CultureInfo CzechCulture = new("cs-CZ");

    public async Task<FoodSummaryPageViewModel> GetPageAsync(
        int? selectedGameId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var games = await db.Games
            .AsNoTracking()
            .OrderByDescending(x => x.StartsAtUtc)
            .Select(x => new FoodSummaryGameOption(x.Id, x.Name, x.StartsAtUtc, x.EndsAtUtc))
            .ToListAsync(cancellationToken);

        if (games.Count == 0)
        {
            return new FoodSummaryPageViewModel([], null, [], [], 0, 0);
        }

        var resolvedGameId = games.Any(x => x.Id == selectedGameId)
            ? selectedGameId!.Value
            : games[0].Id;

        var selectedGame = await db.Games
            .AsNoTracking()
            .Where(x => x.Id == resolvedGameId)
            .Select(x => new FoodSummarySelectedGame(
                x.Id,
                x.Name,
                x.StartsAtUtc,
                x.EndsAtUtc,
                x.MealOrderingClosesAtUtc))
            .SingleAsync(cancellationToken);

        var mealOptions = await db.MealOptions
            .AsNoTracking()
            .Where(x => x.GameId == resolvedGameId && x.IsActive)
            .OrderBy(x => x.Id)
            .Select(x => new FoodSummaryMealOption(x.Id, x.Name, x.Price))
            .ToListAsync(cancellationToken);

        var orderRows = await db.FoodOrders
            .AsNoTracking()
            .Where(x =>
                x.Registration.Submission.GameId == resolvedGameId
                && x.Registration.Submission.Status == SubmissionStatus.Submitted
                && x.Registration.Status == RegistrationStatus.Active
                && x.MealOption.GameId == resolvedGameId
                && x.MealOption.IsActive)
            .Select(x => new FoodSummaryOrderRow(x.RegistrationId, x.MealOptionId, x.MealDayUtc.Date))
            .ToListAsync(cancellationToken);

        var countsByDayAndOption = orderRows
            .GroupBy(x => (x.MealDayUtc, x.MealOptionId))
            .ToDictionary(x => x.Key, x => x.Count());

        var gameDays = EnumerateGameDays(selectedGame.StartsAtUtc, selectedGame.EndsAtUtc);
        var daySummaries = gameDays
            .Select(day =>
            {
                var optionSummaries = mealOptions
                    .Select(option => new FoodSummaryOptionCountViewModel(
                        option.Id,
                        option.Name,
                        option.Price,
                        countsByDayAndOption.GetValueOrDefault((day, option.Id))))
                    .ToList();

                return new FoodSummaryDayViewModel(
                    day,
                    day.ToString("dddd d. M.", CzechCulture),
                    optionSummaries,
                    optionSummaries.Sum(x => x.Count));
            })
            .ToList();

        var overallTotals = mealOptions
            .Select(option => new FoodSummaryOverallTotalViewModel(
                option.Id,
                option.Name,
                daySummaries.Sum(day => day.Options.Single(x => x.MealOptionId == option.Id).Count)))
            .ToList();

        return new FoodSummaryPageViewModel(
            games,
            selectedGame,
            daySummaries,
            overallTotals,
            orderRows.Count,
            orderRows.Select(x => x.RegistrationId).Distinct().Count());
    }

    private static List<DateTime> EnumerateGameDays(DateTime startsAtUtc, DateTime endsAtUtc)
    {
        var days = new List<DateTime>();
        var current = startsAtUtc.Date;
        var end = endsAtUtc.Date;

        while (current <= end)
        {
            days.Add(current);
            current = current.AddDays(1);
        }

        return days;
    }
}

public sealed record FoodSummaryPageViewModel(
    IReadOnlyList<FoodSummaryGameOption> Games,
    FoodSummarySelectedGame? SelectedGame,
    IReadOnlyList<FoodSummaryDayViewModel> Days,
    IReadOnlyList<FoodSummaryOverallTotalViewModel> OverallTotals,
    int TotalSelections,
    int RegistrationsWithOrders);

public sealed record FoodSummaryGameOption(int Id, string Name, DateTime StartsAtUtc, DateTime EndsAtUtc);

public sealed record FoodSummarySelectedGame(
    int Id,
    string Name,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    DateTime MealOrderingClosesAtUtc);

public sealed record FoodSummaryDayViewModel(
    DateTime MealDayUtc,
    string Label,
    IReadOnlyList<FoodSummaryOptionCountViewModel> Options,
    int TotalSelections);

public sealed record FoodSummaryOptionCountViewModel(
    int MealOptionId,
    string MealOptionName,
    decimal Price,
    int Count);

public sealed record FoodSummaryOverallTotalViewModel(
    int MealOptionId,
    string MealOptionName,
    int Count);

internal sealed record FoodSummaryMealOption(int Id, string Name, decimal Price);

internal sealed record FoodSummaryOrderRow(int RegistrationId, int MealOptionId, DateTime MealDayUtc);
