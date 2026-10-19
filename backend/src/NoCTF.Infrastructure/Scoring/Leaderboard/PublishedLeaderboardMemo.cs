using System.Collections.Concurrent;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

/// <summary>
/// Process-local memo of the last published scoreboard bundle. A hit skips the NATS fence read,
/// the distributed payload read and the access-revision query for at most <see cref="Lifetime"/>.
/// Publication notifications raise the minimum accepted version immediately, so a client that
/// receives a scoreboard update never reads an older memoized version afterwards.
/// </summary>
public sealed class PublishedLeaderboardMemo(TimeProvider clock)
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(1);
    private readonly ConcurrentDictionary<Guid, Entry> entries = new();
    private readonly ConcurrentDictionary<Guid, long> minimumVersions = new();

    internal bool TryGet(Guid competitionId, out CachedScoreboardProjection bundle)
    {
        bundle = null!;
        if (!entries.TryGetValue(competitionId, out var entry)
            || clock.GetTimestamp() >= entry.ExpiresAt
            || entry.Bundle.Scoreboard.Snapshot.Version < minimumVersions.GetValueOrDefault(competitionId))
            return false;
        bundle = entry.Bundle;
        return true;
    }

    internal void Set(Guid competitionId, CachedScoreboardProjection bundle)
    {
        var expiresAt = clock.GetTimestamp() + (long)(Lifetime.TotalSeconds * clock.TimestampFrequency);
        entries.AddOrUpdate(competitionId, new Entry(bundle, expiresAt), (_, current) =>
            bundle.Scoreboard.Snapshot.Version >= current.Bundle.Scoreboard.Snapshot.Version
                || clock.GetTimestamp() >= current.ExpiresAt
                ? new Entry(bundle, expiresAt)
                : current);
    }

    /// <summary>Records that <paramref name="version"/> has been published for the competition.</summary>
    public void Published(Guid competitionId, long version) =>
        minimumVersions.AddOrUpdate(competitionId, version, (_, current) => Math.Max(current, version));

    /// <summary>Drops the memoized bundle, for example after the scoreboard access revision changed.</summary>
    public void Invalidate(Guid competitionId) => entries.TryRemove(competitionId, out _);

    private sealed record Entry(CachedScoreboardProjection Bundle, long ExpiresAt);
}
