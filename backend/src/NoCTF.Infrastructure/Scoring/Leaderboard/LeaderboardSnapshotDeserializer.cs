using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

/// <summary>
/// Keeps only the latest decoded payload per competition. The budget is measured in source
/// characters, not managed heap bytes; the payload and object graph are released together.
/// Callers must still verify the authoritative publication and current timing on every read.
/// </summary>
public sealed class LeaderboardSnapshotDeserializer : IDisposable
{
    private readonly MemoryCache snapshots;
    private readonly int maximumPayloadCharacters;
    private readonly AsyncKeyedLock.AsyncKeyedLocker<Guid> decoding = new();

    public LeaderboardSnapshotDeserializer() : this(4 * 1024 * 1024) { }

    internal LeaderboardSnapshotDeserializer(int maximumPayloadCharacters)
    {
        this.maximumPayloadCharacters = maximumPayloadCharacters;
        snapshots = new(new MemoryCacheOptions { SizeLimit = maximumPayloadCharacters });
    }

    internal async ValueTask<CachedScoreboardProjection> ReadAsync(Guid competitionId,
        LeaderboardFencedPayload published, CancellationToken ct)
    {
        using var lease = await decoding.LockAsync(competitionId, ct);
        if (snapshots.TryGetValue<Entry>(competitionId, out var entry)
            && entry is not null && entry.Fence == published.Fence && entry.Payload == published.Payload)
            return entry.Projection;

        var projection = Deserialize(competitionId, published);
        // A new generation always replaces the old one, including an oversized generation.
        snapshots.Remove(competitionId);
        if (published.Payload.Length <= maximumPayloadCharacters)
            snapshots.Set(competitionId, new Entry(published.Fence, published.Payload, projection),
                new MemoryCacheEntryOptions
                {
                    Size = Math.Max(1, published.Payload.Length),
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
                });
        return projection;
    }

    internal static CachedScoreboardProjection Deserialize(Guid competitionId, LeaderboardFencedPayload published)
    {
        var projection = JsonSerializer.Deserialize(published.Payload,
            CachedScoreboardJsonContext.Default.CachedScoreboardProjection);
        if (projection?.Scoreboard?.Snapshot is not { } snapshot
            || snapshot.CompetitionId != competitionId || snapshot.Version != published.Fence)
            throw new InvalidOperationException("The fenced leaderboard payload is invalid.");
        return projection;
    }

    public void Dispose() { snapshots.Dispose(); decoding.Dispose(); }

    private sealed record Entry(long Fence, string Payload, CachedScoreboardProjection Projection);
}
