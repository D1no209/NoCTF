using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Scoring;

public sealed class ScoreboardTrendsTests
{
    [Test]
    public async Task Ctf_trend_accumulates_score_events_and_manual_adjustments()
    {
        var competitionId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var start = DateTimeOffset.Parse("2026-08-27T00:00:00Z");
        var allocationOne = Allocation(teamId, 0, start.AddMinutes(1), 500);
        var allocationTwo = Allocation(teamId, 0, start.AddMinutes(2), -10);
        var reader = new StaticTrendFactReader(
        [
            new(Guid.CreateVersion7(), teamId, start.AddMinutes(3), 25)
        ]);
        var projection = Projection(
            competitionId,
            start.AddMinutes(4),
            [new(teamId, "Team One", "default", 1, ScoreboardRankingState.Eligible, 515, 1, [], [])],
            [allocationOne, allocationTwo]);

        var result = await new BuildScoreboardTrends(reader).ExecuteAsync(projection);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Teams).Count().IsEqualTo(1);
        await Assert.That(result.Teams[0].Points.Select(point => point.Score))
            .IsEquivalentTo([500L, 490L, 515L]);
        await Assert.That(reader.RequestedTeamIds).IsEquivalentTo([teamId]);
    }

    [Test]
    public async Task Final_point_is_reconciled_to_authoritative_score()
    {
        var competitionId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var dataAsOf = DateTimeOffset.Parse("2026-08-27T00:10:00Z");
        var projection = Projection(
            competitionId,
            dataAsOf,
            [new(teamId, "Team One", "default", 1, ScoreboardRankingState.Eligible, 375, 0, [], [])],
            [Allocation(teamId, 0, dataAsOf.AddMinutes(-1), 500)]);

        var result = await new BuildScoreboardTrends(new StaticTrendFactReader([]))
            .ExecuteAsync(projection);

        await Assert.That(result!.Teams[0].Points[^1])
            .IsEqualTo(new ScoreboardTrendPoint(dataAsOf, 375));
    }

    [Test]
    public async Task Empty_team_is_preserved_as_an_empty_series()
    {
        var competitionId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var projection = Projection(
            competitionId,
            DateTimeOffset.UtcNow,
            [new(teamId, "No Score", "default", 1, ScoreboardRankingState.Eligible, 0, 0, [], [])],
            []);

        var result = await new BuildScoreboardTrends(new StaticTrendFactReader([]))
            .ExecuteAsync(projection);

        await Assert.That(result!.Teams[0].Points).IsEmpty();
    }

    [Test]
    public async Task Non_ctf_projection_does_not_build_a_trend()
    {
        var competitionId = Guid.CreateVersion7();
        var projection = Projection(
            competitionId,
            DateTimeOffset.UtcNow,
            [],
            []) with
        {
            Schema = new(competitionId, GameMode.Awdp, 1, 1, [], [])
        };

        var result = await new BuildScoreboardTrends(new StaticTrendFactReader([]))
            .ExecuteAsync(projection);

        await Assert.That(result).IsNull();
    }

    private static ScoreboardProjection Projection(
        Guid competitionId,
        DateTimeOffset dataAsOf,
        IReadOnlyList<ScoreboardTeam> teams,
        IReadOnlyList<ScoreboardEntryAllocation> allocations) => new(
            new(competitionId, 1, []),
            new(competitionId, GameMode.Ctf, 1, 1, [], []),
            new(competitionId, 2, 1, dataAsOf, null, [], teams)
            {
                DataScope = LeaderboardDataScope.Live,
                DataAsOf = dataAsOf
            })
        {
            EntryAllocations = allocations
        };

    private static ScoreboardEntryAllocation Allocation(
        Guid teamId,
        int columnIndex,
        DateTimeOffset occurredAt,
        long netPoints) => new(
            teamId,
            columnIndex,
            new(
                Guid.CreateVersion7(),
                ScoreboardEntryKind.Solve,
                netPoints > 0 ? ScoreboardEntryOutcome.Succeeded : ScoreboardEntryOutcome.Failed,
                null,
                null,
                occurredAt,
                occurredAt,
                Math.Max(0, netPoints),
                Math.Max(0, -netPoints),
                netPoints));

    private sealed class StaticTrendFactReader(
        IReadOnlyList<ScoreboardTrendAdjustment> adjustments) : IScoreboardTrendFactReader
    {
        public IReadOnlyList<Guid> RequestedTeamIds { get; private set; } = [];

        public Task<IReadOnlyList<ScoreboardTrendAdjustment>> ReadManualAdjustmentsAsync(
            Guid competitionId,
            IReadOnlyList<Guid> teamIds,
            DateTimeOffset dataAsOf,
            CancellationToken cancellationToken)
        {
            RequestedTeamIds = teamIds;
            return Task.FromResult(adjustments);
        }
    }
}
