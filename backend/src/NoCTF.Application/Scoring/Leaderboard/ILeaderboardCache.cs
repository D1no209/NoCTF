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
}
public enum LeaderboardProjectionState { Processing }
public sealed record LeaderboardProcessingResponse(
    Guid CompetitionId,
    LeaderboardProjectionState State,
    string StatusUrl);
public sealed record LeaderboardCacheStatus(DateTimeOffset? LastFailureAt);

public interface ILeaderboardCache
{
    Task<LeaderboardResponse?> GetAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<LeaderboardResponse?> GetFrozenAsync(Guid competitionId, CancellationToken cancellationToken) =>
        Task.FromResult<LeaderboardResponse?>(null);
    Task RefreshAsync(Guid competitionId, CancellationToken cancellationToken);
    Task InvalidateAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<LeaderboardCacheStatus> GetStatusAsync(
        Guid competitionId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new LeaderboardCacheStatus(null));
}

public interface ILeaderboardSnapshotFactory
{
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
}
