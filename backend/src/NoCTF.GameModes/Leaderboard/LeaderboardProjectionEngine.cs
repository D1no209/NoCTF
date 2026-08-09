using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;

namespace NoCTF.GameModes.Leaderboard;

public sealed class LeaderboardProjectionEngine(ILeaderboardProjectorCatalog projectors)
    : ILeaderboardProjectionEngine
{
    public LeaderboardProjectionResult Project(LeaderboardProjectionInput input)
    {
        var projection = projectors.Get(input.Mode).Project(input);
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
                        bloodRank);
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
                projection.CurrentScores?.GetValueOrDefault(challenge.Id)))
            .ToList();
        return new(entriesWithCells, challenges);
    }

    private static IReadOnlyList<CtfSolveObservation> BuildCtfSolveObservations(
        LeaderboardProjectionInput input)
    {
        var validTeams = input.Teams
            .Where(team => !team.IsBanned && !team.IsDeleted)
            .Select(team => team.Id)
            .ToHashSet();
        var validChallenges = (input.Challenges ?? [])
            .Where(challenge => !challenge.IsDeleted)
            .Select(challenge => challenge.Id)
            .ToHashSet();
        return input.Submissions
            .Where(fact => validTeams.Contains(fact.TeamId)
                && fact.Kind == SubmissionKind.Flag
                && fact.CompetitionChallengeId is not null
                && (validChallenges.Count == 0
                    || validChallenges.Contains(fact.CompetitionChallengeId.Value))
                && fact.Event is { DeletedAt: null, Result: ScoringResult.Correct })
            .Select(fact => new CtfSolveObservation(
                fact.TeamId,
                fact.CompetitionChallengeId!.Value,
                fact.ReceivedAt,
                fact.SubmissionId))
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
