namespace NoCTF.Application.Scoring.Leaderboard;

public sealed record LeaderboardResponse(Guid CompetitionId, DateTimeOffset GeneratedAt, IReadOnlyList<LeaderboardEntry> Entries)
{
    public IReadOnlyList<LeaderboardSubjectSummary> Subjects { get; init; } = [];
    public IReadOnlyList<LeaderboardBloodSummary> Bloods { get; init; } = [];
    public long SnapshotRevision { get; init; }
    public long TargetRevision { get; init; }
    public bool Stale { get; init; }
    public DateTimeOffset? LastFailureAt { get; init; }
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
    Task RefreshAsync(Guid competitionId, CancellationToken cancellationToken);
    Task InvalidateAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<LeaderboardCacheStatus> GetStatusAsync(
        Guid competitionId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new LeaderboardCacheStatus(0, null));
}
