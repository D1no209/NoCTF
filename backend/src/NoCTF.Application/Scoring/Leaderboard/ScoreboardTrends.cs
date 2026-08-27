using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Scoring.Leaderboard;

public sealed record ScoreboardTrendAdjustment(
    Guid Id,
    Guid TeamId,
    DateTimeOffset OccurredAt,
    long Delta);

public interface IScoreboardTrendFactReader
{
    Task<IReadOnlyList<ScoreboardTrendAdjustment>> ReadManualAdjustmentsAsync(
        Guid competitionId,
        IReadOnlyList<Guid> teamIds,
        DateTimeOffset dataAsOf,
        CancellationToken cancellationToken);
}

public sealed record ScoreboardTrendPoint(DateTimeOffset At, long Score);

public sealed record ScoreboardTeamTrend(
    Guid TeamId,
    string TeamName,
    string TrackKey,
    IReadOnlyList<ScoreboardTrendPoint> Points);

public sealed record ScoreboardTrends(
    Guid CompetitionId,
    long Version,
    DateTimeOffset GeneratedAt,
    DateTimeOffset DataAsOf,
    IReadOnlyList<ScoreboardTeamTrend> Teams);

public sealed class BuildScoreboardTrends(IScoreboardTrendFactReader facts)
{
    public async Task<ScoreboardTrends?> ExecuteAsync(
        ScoreboardProjection projection,
        CancellationToken cancellationToken = default)
    {
        if (projection.Schema.Mode != GameMode.Ctf)
            return null;

        var snapshot = projection.Snapshot;
        var dataAsOf = snapshot.DataAsOf ?? snapshot.GeneratedAt;
        var teamIds = snapshot.Teams.Select(team => team.TeamId).ToArray();
        var teamIdSet = teamIds.ToHashSet();
        var adjustments = teamIds.Length == 0
            ? []
            : await facts.ReadManualAdjustmentsAsync(
                snapshot.CompetitionId,
                teamIds,
                dataAsOf,
                cancellationToken);
        var adjustmentsByTeam = adjustments
            .GroupBy(adjustment => adjustment.TeamId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var allocationsByTeam = projection.EntryAllocations
            .Where(allocation => teamIdSet.Contains(allocation.TeamId)
                && allocation.Entry.NetPoints.GetValueOrDefault() != 0
                && allocation.Entry.OccurredAt <= dataAsOf)
            .GroupBy(allocation => allocation.TeamId)
            .ToDictionary(group => group.Key, group => group
                .DistinctBy(allocation => allocation.Entry.Id)
                .ToArray());

        var teams = snapshot.Teams.Select(team =>
        {
            var deltas = allocationsByTeam.GetValueOrDefault(team.TeamId, [])
                .Select(allocation => new TrendDelta(
                    allocation.Entry.Id,
                    allocation.Entry.SettledAt ?? allocation.Entry.OccurredAt,
                    allocation.Entry.NetPoints.GetValueOrDefault()))
                .Concat(adjustmentsByTeam.GetValueOrDefault(team.TeamId, [])
                    .Select(adjustment => new TrendDelta(
                        adjustment.Id,
                        adjustment.OccurredAt,
                        adjustment.Delta)))
                .Where(delta => delta.At <= dataAsOf && delta.Value != 0)
                .OrderBy(delta => delta.At)
                .ThenBy(delta => delta.Id)
                .GroupBy(delta => delta.At)
                .Select(group => new
                {
                    At = group.Key,
                    Delta = group.Aggregate(0L, (total, delta) => checked(total + delta.Value))
                })
                .ToArray();
            var points = new List<ScoreboardTrendPoint>(deltas.Length + 1);
            var score = 0L;
            foreach (var delta in deltas)
            {
                score = checked(score + delta.Delta);
                points.Add(new(delta.At, score));
            }

            // Dynamic score recalculation, bans and corrections may change the authoritative
            // total without introducing another score-bearing fact. Anchor the final point to
            // the same total shown by the scoreboard instead of exposing a divergent chart.
            if (score != team.TotalScore)
            {
                if (points.Count > 0 && points[^1].At == dataAsOf)
                    points[^1] = new(dataAsOf, team.TotalScore);
                else
                    points.Add(new(dataAsOf, team.TotalScore));
            }

            return new ScoreboardTeamTrend(
                team.TeamId,
                team.TeamName,
                team.TrackKey,
                points);
        }).ToArray();

        return new(
            snapshot.CompetitionId,
            snapshot.Version,
            snapshot.GeneratedAt,
            dataAsOf,
            teams);
    }

    private sealed record TrendDelta(Guid Id, DateTimeOffset At, long Value);
}
