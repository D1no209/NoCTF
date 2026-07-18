using NoCTF.Application.Scoring.Events;
using NoCTF.Application.Scoring.Evaluation;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Submissions.Events;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Leaderboard;

public static class GameModeLeaderboardProjectorCatalog
{
    private static readonly IReadOnlyDictionary<GameMode, IGameModeLeaderboardProjector> Projectors =
        new Dictionary<GameMode, IGameModeLeaderboardProjector>
        {
            [GameMode.Ctf] = new CtfLeaderboardProjector(),
            [GameMode.Awd] = new AwdLeaderboardProjector(),
            [GameMode.Awdp] = new AwdpLeaderboardProjector(),
            [GameMode.Koh] = new KohLeaderboardProjector(),
            [GameMode.Penetration] = new PenetrationLeaderboardProjector()
        };

    public static IGameModeLeaderboardProjector Get(GameMode mode) =>
        Projectors.TryGetValue(mode, out var projector)
            ? projector
            : throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported game mode.");
}

public abstract class ModeLeaderboardProjectorBase : IGameModeLeaderboardProjector
{
    public abstract GameMode Mode { get; }

    protected abstract LeaderboardAchievement? MapAchievement(IScoringStreamEvent @event);

    public LeaderboardSnapshot Project(
        ScoringContext context,
        long projectionVersion,
        IReadOnlyList<IScoringStreamEvent> events) =>
        LeaderboardProjectionReducer.Project(context, projectionVersion, events, MapAchievement);
}

public sealed record LeaderboardAchievement(Guid TeamId, Guid ChallengeId, DateTimeOffset AchievedAt);

public sealed class CtfLeaderboardProjector : ModeLeaderboardProjectorBase
{
    public override GameMode Mode => GameMode.Ctf;
    protected override LeaderboardAchievement? MapAchievement(IScoringStreamEvent @event) => @event switch
    {
        CtfSolveRecorded item => new(item.TeamId, item.ChallengeId, item.SolvedAt),
        _ => null
    };
}

public sealed class AwdLeaderboardProjector : ModeLeaderboardProjectorBase
{
    public override GameMode Mode => GameMode.Awd;
    protected override LeaderboardAchievement? MapAchievement(IScoringStreamEvent @event) => @event switch
    {
        AwdAttackRewarded item => new(item.AttackerTeamId, item.ChallengeId, item.ScoredAt),
        _ => null
    };
}

public sealed class AwdpLeaderboardProjector : ModeLeaderboardProjectorBase
{
    public override GameMode Mode => GameMode.Awdp;
    protected override LeaderboardAchievement? MapAchievement(IScoringStreamEvent @event) => @event switch
    {
        AwdpBreakAchieved item => new(item.TeamId, item.ChallengeId, item.ScoredAt),
        AwdpFixAchieved item => new(item.TeamId, item.ChallengeId, item.ScoredAt),
        _ => null
    };
}

public sealed class KohLeaderboardProjector : ModeLeaderboardProjectorBase
{
    public override GameMode Mode => GameMode.Koh;
    protected override LeaderboardAchievement? MapAchievement(IScoringStreamEvent @event) => @event switch
    {
        KohControlIntervalScored item => new(item.TeamId, item.ChallengeId, item.ScoredAt),
        _ => null
    };
}

public sealed class PenetrationLeaderboardProjector : ModeLeaderboardProjectorBase
{
    public override GameMode Mode => GameMode.Penetration;
    protected override LeaderboardAchievement? MapAchievement(IScoringStreamEvent @event) => @event switch
    {
        PenetrationStageScored item => new(item.TeamId, item.ChallengeId, item.ScoredAt),
        _ => null
    };
}

internal static class LeaderboardProjectionReducer
{
    public static LeaderboardSnapshot Project(
        ScoringContext context,
        long projectionVersion,
        IReadOnlyList<IScoringStreamEvent> events,
        Func<IScoringStreamEvent, LeaderboardAchievement?> mapAchievement)
    {
        var scores = new Dictionary<Guid, long>();
        var solves = new Dictionary<Guid, List<(Guid ChallengeId, DateTimeOffset SolvedAt)>>();
        foreach (var @event in events)
        {
            switch (@event)
            {
                case ScoreAwarded awarded:
                    scores[awarded.TeamId] = scores.GetValueOrDefault(awarded.TeamId) + awarded.Points;
                    break;
                case ScoreDeducted deducted:
                    scores[deducted.TeamId] = scores.GetValueOrDefault(deducted.TeamId) - deducted.Points;
                    break;
            }

            var achievement = mapAchievement(@event);
            if (achievement is null)
                continue;
            solves.GetOrAdd(achievement.TeamId).Add((achievement.ChallengeId, achievement.AchievedAt));
        }

        var ranked = context.Teams.Values
            .Where(team => !team.IsBanned && !team.IsDeleted)
            .Select(team =>
            {
                var teamSolves = solves.GetValueOrDefault(team.Id) ?? [];
                return new
                {
                    team.Id,
                    team.Name,
                    Score = scores.GetValueOrDefault(team.Id),
                    Solves = teamSolves
                };
            })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Solves.Count)
            .ThenBy(item => item.Solves.Count == 0 ? DateTimeOffset.MaxValue : item.Solves.Max(solve => solve.SolvedAt))
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToList();

        var entries = ranked.Select((item, index) => new LeaderboardEntry(
            index + 1,
            item.Id,
            item.Name,
            item.Score,
            item.Solves.Count,
            item.Solves.Count == 0 ? null : item.Solves.Max(solve => solve.SolvedAt),
            item.Solves
                .GroupBy(solve => solve.ChallengeId)
                .Select(group => new LeaderboardChallengeSummary(
                    group.Key,
                    context.Challenges.GetValueOrDefault(group.Key)?.Direction ?? string.Empty,
                    group.Count()))
                .ToList())).ToList();

        return new(context.CompetitionId, projectionVersion, DateTimeOffset.UtcNow, entries);
    }

    private static List<T> GetOrAdd<TKey, T>(this Dictionary<TKey, List<T>> dictionary, TKey key)
        where TKey : notnull
    {
        if (!dictionary.TryGetValue(key, out var value))
        {
            value = [];
            dictionary[key] = value;
        }
        return value;
    }
}
