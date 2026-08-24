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

public sealed record LeaderboardResponse(Guid CompetitionId, DateTimeOffset GeneratedAt, IReadOnlyList<LeaderboardEntry> Entries)
{
    public IReadOnlyList<LeaderboardChallengeInfo> Challenges { get; init; } = [];
    public IReadOnlyList<LeaderboardTrackInfo> Tracks { get; init; } = [];
    public CompetitionLeaderboardVisibility Visibility { get; init; }
    public LeaderboardDataScope DataScope { get; init; }
    public DateTimeOffset? DataAsOf { get; init; }
    public int? CurrentRound { get; init; }
    public int? SettledThroughRound { get; init; }
    public int? RoundDurationSeconds { get; init; }
    public int? CurrentRoundRemainingSeconds { get; init; }
}
public enum LeaderboardProjectionState { Processing }
public sealed record LeaderboardProcessingResponse(
    Guid CompetitionId,
    LeaderboardProjectionState State,
    string StatusUrl);
public sealed record LeaderboardCacheStatus(DateTimeOffset? LastFailureAt);

/// <summary>
/// One atomic projection generation. The legacy projection remains an internal scoring
/// dependency while the public protocol exposes only the normalized scoreboard.
/// </summary>
public sealed record LeaderboardProjectionBundle(
    LeaderboardResponse Legacy,
    ScoreboardProjection Scoreboard)
{
    /// <summary>
    /// The first wall-clock boundary after which this time-derived projection must be
    /// rebuilt even when no business event has been committed. This is cache metadata,
    /// not persisted scheduling state.
    /// </summary>
    public DateTimeOffset? ValidUntil { get; init; }
}

public interface ILeaderboardCache
{
    Task<LeaderboardResponse?> GetAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<LeaderboardResponse?> GetFrozenAsync(Guid competitionId, CancellationToken cancellationToken) =>
        Task.FromResult<LeaderboardResponse?>(null);
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
    Task<LeaderboardProjectionBundle?> CreateBundleAsync(
        Guid competitionId,
        DateTimeOffset projectedAt,
        CancellationToken cancellationToken);

    Task<LeaderboardResponse?> CreateAsync(
        Guid competitionId,
        DateTimeOffset projectedAt,
        CancellationToken cancellationToken);

    Task<LeaderboardResponse?> CreateWithConfigurationAsync(
        Guid competitionId,
        string competitionConfigurationJson,
        DateTimeOffset projectedAt,
        CancellationToken cancellationToken) =>
        CreateAsync(competitionId, projectedAt, cancellationToken);

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
