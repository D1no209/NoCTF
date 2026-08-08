using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Scoring.Leaderboard;

public enum LeaderboardDataScope
{
    Live,
    Frozen,
    Hidden
}

public sealed record LeaderboardResponse(Guid CompetitionId, DateTimeOffset GeneratedAt, IReadOnlyList<LeaderboardEntry> Entries)
{
    public IReadOnlyList<LeaderboardSubjectSummary> Subjects { get; init; } = [];
    public IReadOnlyList<LeaderboardBloodSummary> Bloods { get; init; } = [];
    public IReadOnlyList<LeaderboardTeamSeries> Series { get; init; } = [];
    public IReadOnlyList<LeaderboardChallengeInfo> Challenges { get; init; } = [];
    public long SnapshotRevision { get; init; }
    public long TargetRevision { get; init; }
    public bool Stale { get; init; }
    public DateTimeOffset? LastFailureAt { get; init; }
    public CompetitionLeaderboardVisibility Visibility { get; init; }
    public LeaderboardDataScope DataScope { get; init; }
    public DateTimeOffset? DataAsOf { get; init; }
}
public enum LeaderboardProjectionState { Processing }
public sealed record LeaderboardProcessingResponse(
    Guid CompetitionId,
    LeaderboardProjectionState State,
    long TargetRevision,
    string StatusUrl);
public sealed record LeaderboardCacheStatus(long TargetRevision, DateTimeOffset? LastFailureAt);

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
        Task.FromResult(new LeaderboardCacheStatus(0, null));
}

public interface ILeaderboardSnapshotFactory
{
    Task<LeaderboardResponse?> CreateAsync(
        Guid competitionId,
        DateTimeOffset projectedAt,
        bool historical,
        CancellationToken cancellationToken);
}
