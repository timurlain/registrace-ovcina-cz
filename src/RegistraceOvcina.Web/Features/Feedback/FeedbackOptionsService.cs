using Microsoft.EntityFrameworkCore;
using RegistraceOvcina.Web.Data;

namespace RegistraceOvcina.Web.Features.Feedback;

/// <summary>
/// Read-only service that loads the per-game feedback question JSON columns
/// from <see cref="Game"/> and parses them into a
/// <see cref="FeedbackQuestionSet"/> via <see cref="FeedbackQuestionParser"/>.
/// </summary>
/// <remarks>
/// <para>The kid set is used for <see cref="AttendeeType.Player"/>
/// registrations; the adult set is used for everyone else
/// (<see cref="AttendeeType.Adult"/>).</para>
/// <para>If a game has no JSON configured (column is null) the service returns
/// <see cref="FeedbackQuestionSet.Empty"/> rather than throwing — callers are
/// expected to short-circuit feedback emails / forms when the set is empty.
/// Likewise, requesting an unknown <c>gameId</c> returns the empty pair from
/// <see cref="GetBothSetsAsync"/> instead of failing.</para>
/// </remarks>
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
