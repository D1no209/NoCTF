using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;

namespace NoCTF.GameModes.Leaderboard;

/// <summary>Shared deterministic fact projector. Game modes can specialize this strategy without persistence changes.</summary>
public abstract class FactLeaderboardProjector(GameMode mode) : IGameModeLeaderboardProjector
{
    public GameMode Mode => mode;

    public IReadOnlyList<LeaderboardEntry> Project(LeaderboardProjectionInput input)
    {
        var validTeams = input.Teams.Where(x => !x.IsBanned && !x.IsDeleted).ToDictionary(x => x.Id);
        var facts = input.Submissions
            .Where(x => validTeams.ContainsKey(x.TeamId) && !x.Event.IsDeleted && x.Event.Result == ScoringResult.Correct)
            .OrderBy(x => x.ReceivedAt).ThenBy(x => x.SubmissionId)
            .ToList();
        var system = input.SystemEvents
            .Where(x => !x.Event.IsDeleted && x.Event.Result == ScoringResult.Correct)
            .OrderBy(x => x.Event.OccurredAt).ThenBy(x => x.Event.Id)
            .ToList();
        var rows = validTeams.Values.Select(team =>
        {
            var own = facts.Where(x => x.TeamId == team.Id).ToList();
            var ownSystem = system.Where(x => x.Event.TeamId == team.Id).ToList();
            var challenges = own.Where(x => x.ChallengeId.HasValue).GroupBy(x => x.ChallengeId!.Value)
                .Select(g => new LeaderboardChallengeSummary(g.Key, string.Empty, g.Count())).ToList();
            var last = own.Select(x => x.Event.OccurredAt).Concat(ownSystem.Select(x => x.Event.OccurredAt)).OrderByDescending(x => x).FirstOrDefault();
            return new LeaderboardEntry(0, team.Id, team.Name, own.Count + ownSystem.Count, own.Count, last == default ? null : last, challenges);
        }).OrderByDescending(x => x.Score).ThenByDescending(x => x.SolveCount).ThenBy(x => x.LastScoreAt ?? DateTimeOffset.MaxValue).ThenBy(x => x.TeamName, StringComparer.Ordinal).ToList();
        return rows.Select((x, i) => x with { Rank = i + 1 }).ToList();
    }
}

public sealed class CtfLeaderboardProjector() : FactLeaderboardProjector(GameMode.Ctf);
public sealed class AwdLeaderboardProjector() : FactLeaderboardProjector(GameMode.Awd);
public sealed class AwdpLeaderboardProjector() : FactLeaderboardProjector(GameMode.Awdp);
public sealed class KohLeaderboardProjector() : FactLeaderboardProjector(GameMode.Koh);
public sealed class PenetrationLeaderboardProjector() : FactLeaderboardProjector(GameMode.Penetration);
