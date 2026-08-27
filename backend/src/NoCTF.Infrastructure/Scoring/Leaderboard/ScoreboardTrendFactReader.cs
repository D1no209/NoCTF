using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed class ScoreboardTrendFactReader(NoCtfDbContext db) : IScoreboardTrendFactReader
{
    public async Task<IReadOnlyList<ScoreboardTrendAdjustment>> ReadManualAdjustmentsAsync(
        Guid competitionId,
        IReadOnlyList<Guid> teamIds,
        DateTimeOffset dataAsOf,
        CancellationToken cancellationToken)
    {
        if (teamIds.Count == 0)
            return [];

        var rows = await db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.CompetitionId == competitionId
                && fact.TeamId != null
                && teamIds.Contains(fact.TeamId.Value)
                && fact.Kind == GameplayFactKind.ManualAdjustment
                && fact.State == GameplayFactState.Completed
                && fact.Result == GameplayFactResult.Applied
                && fact.OccurredAt <= dataAsOf)
            .OrderBy(fact => fact.OccurredAt)
            .ThenBy(fact => fact.Id)
            .Select(fact => new
            {
                fact.Id,
                TeamId = fact.TeamId!.Value,
                fact.OccurredAt,
                fact.Value
            })
            .ToArrayAsync(cancellationToken);

        return rows.Select(row => new ScoreboardTrendAdjustment(
            row.Id,
            row.TeamId,
            row.OccurredAt,
            long.Parse(row.Value!, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)))
            .ToArray();
    }
}
