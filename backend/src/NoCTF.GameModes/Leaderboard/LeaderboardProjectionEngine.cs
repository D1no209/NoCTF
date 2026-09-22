using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;

namespace NoCTF.GameModes.Leaderboard;

public sealed class LeaderboardProjectionEngine(
    ILeaderboardProjectorCatalog projectors,
    TimeProvider? clock = null)
    : ILeaderboardProjectionEngine
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;

    public ScoreboardProjection Project(LeaderboardProjectionInput input)
    {
        input = input with { ProjectedAt = input.ProjectedAt ?? timeProvider.GetUtcNow() };
        var projection = projectors.Get(input.Mode).Project(input);
        var aggregate = ProjectAggregate(input, projection);
        return NormalizedScoreboardProjection.Project(input, aggregate);
    }

    private static LeaderboardAggregateProjection ProjectAggregate(
        LeaderboardProjectionInput input,
        GameModeLeaderboardProjection projection)
    {
        var entries = projection.Entries;
        var bloods = input.Mode == GameMode.Ctf
            ? BuildBloodRanks(BuildCtfSolveObservations(input))
            : new Dictionary<(Guid TeamId, Guid ChallengeId), LeaderboardBloodRank>();
        var cellsByTeam = projection.Cells
            .GroupBy(item => item.TeamId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var entriesWithCells = entries.Select(entry =>
        {
            var cells = cellsByTeam.GetValueOrDefault(entry.TeamId, [])
                .GroupBy(item => item.CompetitionChallengeId)
                .Select(group =>
                {
                    var item = group.First();
                    var bloodRank = bloods.TryGetValue(
                        (entry.TeamId, item.CompetitionChallengeId),
                        out var value)
                        ? value
                        : (LeaderboardBloodRank?)null;
                    return new LeaderboardCell(
                        item.CompetitionChallengeId,
                        item.Score,
                        item.SolvedAt,
                        item.SolverName,
                        bloodRank)
                    {
                        AttackScore = item.AttackScore,
                        DefenseScore = item.DefenseScore
                    };
                })
                .OrderBy(item => item.CompetitionChallengeId)
                .ToList();
            return entry with { Cells = cells };
        }).ToList();
        var challenges = (input.Challenges ?? [])
            .Where(challenge => !challenge.IsDeleted)
            .Select(challenge => new LeaderboardChallengeInfo(
                challenge.Id,
                challenge.Title,
                challenge.Direction,
                projection.CurrentScores?.GetValueOrDefault(challenge.Id),
                projection.CurrentBreakScores?.GetValueOrDefault(challenge.Id),
                projection.CurrentFixScores?.GetValueOrDefault(challenge.Id)))
            .ToList();
        return new LeaderboardAggregateProjection(
            entriesWithCells,
            challenges,
            projection.CurrentRound,
            projection.SettledThroughRound,
            projection.RoundDurationSeconds,
            projection.CurrentRoundRemainingSeconds);
    }

    private static IReadOnlyList<CtfSolveObservation> BuildCtfSolveObservations(
        LeaderboardProjectionInput input)
    {
        var validTeams = input.Teams
            .Where(CtfCompletionEligibility.EarnsBlood)
            .Select(team => team.Id)
            .ToHashSet();
        var validChallenges = (input.Challenges ?? [])
            .Where(challenge => !challenge.IsDeleted)
            .ToDictionary(challenge => challenge.Id);
        return input.GameplayFacts
            .Where(fact => fact.TeamId is Guid teamId && validTeams.Contains(teamId)
                && fact.CompetitionChallengeId is not null
                && (validChallenges.Count == 0 ? fact.Kind == GameplayFactKind.FlagAttempt
                    : validChallenges.TryGetValue(fact.CompetitionChallengeId.Value, out var challenge)
                        && CtfCompletionEligibility.Matches(fact.Kind, challenge.InteractionKind))
                && fact.Result == GameplayFactResult.Correct)
            .Select(fact => new CtfSolveObservation(
                fact.TeamId!.Value,
                fact.CompetitionChallengeId!.Value,
                fact.OccurredAt,
                fact.GameplayFactId))
            .ToList();
    }

    private static IReadOnlyDictionary<(Guid TeamId, Guid ChallengeId), LeaderboardBloodRank> BuildBloodRanks(
        IReadOnlyList<CtfSolveObservation> observations) => observations
            .GroupBy(item => item.ChallengeId)
            .SelectMany(group => group
                .GroupBy(item => item.TeamId)
                .Select(team => team
                    .OrderBy(item => item.OccurredAt)
                    .ThenBy(item => item.StableId)
                    .First())
                .OrderBy(item => item.OccurredAt)
                .ThenBy(item => item.StableId)
                .Take(3)
                .Select((item, index) => new
                {
                    item.TeamId,
                    ChallengeId = group.Key,
                    BloodRank = (LeaderboardBloodRank)(index + 1)
                }))
            .ToDictionary(
                item => (item.TeamId, item.ChallengeId),
                item => item.BloodRank);

    private sealed record CtfSolveObservation(
        Guid TeamId,
        Guid ChallengeId,
        DateTimeOffset OccurredAt,
        Guid StableId);
}
