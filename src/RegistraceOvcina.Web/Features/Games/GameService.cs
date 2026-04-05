using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;
using RegistraceOvcina.Web.Infrastructure;

namespace RegistraceOvcina.Web.Features.Games;

public sealed class GameService(IDbContextFactory<ApplicationDbContext> dbContextFactory, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<GameSummary>> GetLandingGamesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var games = await db.Games
            .AsNoTracking()
            .Where(x => x.IsPublished)
            .OrderByDescending(x => x.StartsAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Description,
                x.StartsAtUtc,
                x.EndsAtUtc,
                x.RegistrationClosesAtUtc,
                x.TargetPlayerCountTotal,
                ReservedPlayers = x.Submissions
                    .Where(s => s.Status == SubmissionStatus.Submitted)
                    .SelectMany(s => s.Registrations)
                    .Count(r => r.Status == RegistrationStatus.Active && r.AttendeeType == AttendeeType.Player)
            })
            .ToListAsync(cancellationToken);

        return games
            .Select(x => new GameSummary(
                x.Id,
                x.Name,
                x.Description,
                x.StartsAtUtc,
                x.EndsAtUtc,
                x.RegistrationClosesAtUtc,
                x.TargetPlayerCountTotal,
                Math.Max(0, x.TargetPlayerCountTotal - x.ReservedPlayers),
                true))
            .ToList();
    }

    public async Task<IReadOnlyList<GameSummary>> GetAdminGamesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var games = await db.Games
            .AsNoTracking()
            .OrderByDescending(x => x.StartsAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Description,
                x.StartsAtUtc,
                x.EndsAtUtc,
                x.RegistrationClosesAtUtc,
                x.TargetPlayerCountTotal,
                x.IsPublished,
                ReservedPlayers = x.Submissions
                    .Where(s => s.Status == SubmissionStatus.Submitted)
                    .SelectMany(s => s.Registrations)
                    .Count(r => r.Status == RegistrationStatus.Active && r.AttendeeType == AttendeeType.Player)
            })
            .ToListAsync(cancellationToken);

        return games
            .Select(x => new GameSummary(
                x.Id,
                x.Name,
                x.Description,
                x.StartsAtUtc,
                x.EndsAtUtc,
                x.RegistrationClosesAtUtc,
                x.TargetPlayerCountTotal,
                Math.Max(0, x.TargetPlayerCountTotal - x.ReservedPlayers),
                x.IsPublished))
            .ToList();
    }

    public async Task<int> CreateGameAsync(CreateGameCommand command, string actorUserId, CancellationToken cancellationToken = default)
    {
        ValidateSchedule(command);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var game = new Game
        {
            Name = command.Name.Trim(),
            Description = command.Description?.Trim(),
            StartsAtUtc = CzechTime.ToUtc(command.StartsAtLocal),
            EndsAtUtc = CzechTime.ToUtc(command.EndsAtLocal),
            RegistrationClosesAtUtc = CzechTime.ToUtc(command.RegistrationClosesAtLocal),
            MealOrderingClosesAtUtc = CzechTime.ToUtc(command.MealOrderingClosesAtLocal),
            PaymentDueAtUtc = CzechTime.ToUtc(command.PaymentDueAtLocal),
            AssignmentFreezeAtUtc = command.AssignmentFreezeAtLocal is { } assignmentFreeze
                ? CzechTime.ToUtc(assignmentFreeze)
                : null,
            PlayerBasePrice = command.PlayerBasePrice,
            AdultHelperBasePrice = command.AdultHelperBasePrice,
            BankAccount = command.BankAccount.Trim(),
            BankAccountName = command.BankAccountName.Trim(),
            VariableSymbolStrategy = VariableSymbolStrategy.PerSubmissionId,
            TargetPlayerCountTotal = command.TargetPlayerCountTotal,
            IsPublished = command.IsPublished,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        db.Games.Add(game);
        await db.SaveChangesAsync(cancellationToken);

        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(Game),
            EntityId = game.Id.ToString(),
            Action = "GameCreated",
            ActorUserId = actorUserId,
            CreatedAtUtc = nowUtc,
            DetailsJson = JsonSerializer.Serialize(new
            {
                game.Name,
                game.IsPublished,
                game.TargetPlayerCountTotal
            })
        });

        await db.SaveChangesAsync(cancellationToken);
        return game.Id;
    }

    private static void ValidateSchedule(CreateGameCommand command)
    {
        if (command.EndsAtLocal <= command.StartsAtLocal)
        {
            throw new ValidationException("Konec hry musí být po začátku.");
        }

        if (command.RegistrationClosesAtLocal > command.StartsAtLocal)
        {
            throw new ValidationException("Uzávěrka registrace musí být nejpozději v okamžiku začátku hry.");
        }

        if (command.MealOrderingClosesAtLocal > command.StartsAtLocal)
        {
            throw new ValidationException("Uzávěrka jídel musí být nejpozději v okamžiku začátku hry.");
        }
    }
}

public sealed record CreateGameCommand(
    string Name,
    string? Description,
    DateTime StartsAtLocal,
    DateTime EndsAtLocal,
    DateTime RegistrationClosesAtLocal,
    DateTime MealOrderingClosesAtLocal,
    DateTime PaymentDueAtLocal,
    DateTime? AssignmentFreezeAtLocal,
    decimal PlayerBasePrice,
    decimal AdultHelperBasePrice,
    string BankAccount,
    string BankAccountName,
    int TargetPlayerCountTotal,
    bool IsPublished);

public sealed record GameSummary(
    int Id,
    string Name,
    string? Description,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    DateTime RegistrationClosesAtUtc,
    int TargetPlayerCountTotal,
    int RemainingPlayerSpots,
    bool IsPublished);
