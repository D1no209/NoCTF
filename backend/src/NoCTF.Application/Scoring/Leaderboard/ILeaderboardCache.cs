using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Scoring.Leaderboard;

public enum LeaderboardDataScope
{
    Live,
    Frozen,
    Hidden
}

public sealed record LeaderboardTrackInfo(
    string Key,
    string Name,
    bool IsInternal,
    bool VisibleOnLeaderboard);

public enum LeaderboardProjectionState { Processing }
public sealed record LeaderboardProcessingResponse(
    Guid CompetitionId,
    LeaderboardProjectionState State,
    string StatusUrl);
public sealed record LeaderboardCacheStatus(DateTimeOffset? LastFailureAt);

public interface ILeaderboardCache
{
    Task<ScoreboardProjection?> GetScoreboardAsync(Guid competitionId, CancellationToken cancellationToken) =>
        Task.FromResult<ScoreboardProjection?>(null);
    Task<ScoreboardProjection?> GetFrozenScoreboardAsync(Guid competitionId, CancellationToken cancellationToken) =>
        Task.FromResult<ScoreboardProjection?>(null);
    Task RefreshAsync(Guid competitionId, CancellationToken cancellationToken);
    Task InvalidateAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<LeaderboardCacheStatus> GetStatusAsync(
        Guid competitionId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new LeaderboardCacheStatus(null));
}

public interface ILeaderboardSnapshotFactory
{
    Task<ScoreboardProjection?> CreateScoreboardAsync(
        Guid competitionId,
        DateTimeOffset projectedAt,
        CancellationToken cancellationToken) =>
        Task.FromResult<ScoreboardProjection?>(null);

    Task<ScoreboardProjection?> CreateScoreboardWindowAsync(
        Guid competitionId,
        int endingRound,
        DateTimeOffset projectedAt,
        CancellationToken cancellationToken) =>
        Task.FromResult<ScoreboardProjection?>(null);
}
