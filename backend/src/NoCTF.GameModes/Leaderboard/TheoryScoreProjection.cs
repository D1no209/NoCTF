using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Theory.Activities;

namespace NoCTF.GameModes.Leaderboard;

internal static class TheoryScoreProjection
{
    internal static ScoreboardProjection Apply(LeaderboardProjectionInput input, GameModeLeaderboardProjection raw, ScoreboardProjection board)
    {
        if (input.TheoryScores is null || !TheoryActivityPolicy.Supports(input.Mode)) return board;
        var allowed = input.Teams.Where(team => !team.IsBanned && !team.IsDeleted && team.EarnsScore).Select(team => team.Id).ToHashSet();
        var scores = input.TheoryScores.Where(score => allowed.Contains(score.TeamId)).ToDictionary(score => score.TeamId);
        var updated = board.Snapshot.Teams.Select(team => scores.TryGetValue(team.TeamId, out var score)
            ? team with { TheoryScore = score.Points, TheoryAchievedAt = score.AchievedAt, HasTheorySubmission = score.HasSubmission,
                TotalScore = checked(team.TotalScore + score.Points) }
            : team).ToArray();
        var ranked = updated.Where(team => team.Rank is not null && team.RankingState == ScoreboardRankingState.Eligible)
            .GroupBy(team => team.TrackKey, StringComparer.OrdinalIgnoreCase)
            .SelectMany(track => WriteUpBenefitProjection.Rank(input, raw, track).Select((team, index) => team with { Rank = index + 1 }))
            .ToDictionary(team => team.TeamId);
        var teams = updated.Select(team => ranked.GetValueOrDefault(team.TeamId, team)).ToArray();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(board.Snapshot.Version + ":theory:" + string.Join(';', teams.OrderBy(team => team.TeamId)
            .Select(team => $"{team.TeamId:N}:{team.TheoryScore}:{team.HasTheorySubmission}:{team.TheoryAchievedAt?.UtcTicks}:{team.Rank}"))));
        return board with { Snapshot = board.Snapshot with { Teams = teams, TheoryEnabled = true,
            Version = Math.Max(1, BitConverter.ToInt64(digest) & long.MaxValue) } };
    }
}
