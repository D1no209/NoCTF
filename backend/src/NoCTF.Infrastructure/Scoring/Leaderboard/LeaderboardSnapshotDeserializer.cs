using System.Text.Json;
using System.Numerics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Observability;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

/// <summary>
/// 缓存经过验证的不可变发布版本。预算使用源内容字符数量，
/// 缓存同时持有源内容和对象，因此该预算不代表托管内存字节数。
/// </summary>
public sealed class LeaderboardSnapshotDeserializer : IDisposable
{
    private readonly MemoryCache snapshots;
    private readonly int maximumPayloadCharacters;
    private readonly object slotsGate = new();

    public LeaderboardSnapshotDeserializer() : this(64 * 1024 * 1024) { }

    public LeaderboardSnapshotDeserializer(IConfiguration configuration)
        : this(configuration.GetValue("Caching:LeaderboardDecodedBudgetCharacters", 64 * 1024 * 1024)) { }

    internal LeaderboardSnapshotDeserializer(int maximumPayloadCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPayloadCharacters);
        this.maximumPayloadCharacters = maximumPayloadCharacters;
        snapshots = new(new MemoryCacheOptions { SizeLimit = maximumPayloadCharacters });
    }

    internal ValueTask<CachedScoreboardProjection> ReadAsync(Guid competitionId,
        LeaderboardFencedPayload published, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (published.Payload.Length > maximumPayloadCharacters)
        {
            NoCtfTelemetry.RecordLeaderboardDecodedCache(false, published.Payload.Length);
            return ValueTask.FromResult(Decode());
        }

        snapshots.TryGetValue(competitionId, out PublicationSlot? slot);
        // Only cache metadata is synchronized. Parsing and all business reads run outside this section.
        if (slot is null || slot.Capacity < published.Payload.Length)
        {
            lock (slotsGate)
            {
                if (!snapshots.TryGetValue(competitionId, out slot)
                    || slot is null || slot.Capacity < published.Payload.Length)
                {
                    var capacity = Math.Min((long)maximumPayloadCharacters,
                        BitOperations.RoundUpToPowerOf2((uint)published.Payload.Length));
                    var previous = slot?.Read();
                    slot = new PublicationSlot(capacity);
                    if (previous is not null) slot.GetOrInstall(previous);
                    snapshots.Remove(competitionId);
                    snapshots.Set(competitionId, slot,
                    new MemoryCacheEntryOptions
                    {
                        Size = capacity,
                        SlidingExpiration = TimeSpan.FromMinutes(1)
                    });
                }
            }
        }
        var cached = slot.Read();
        if (cached is not null && cached.Fence == published.Fence && cached.Payload == published.Payload)
        {
            NoCtfTelemetry.RecordLeaderboardDecodedCache(true, published.Payload.Length);
            return ValueTask.FromResult(cached.Projection.Value);
        }
        var candidate = new Entry(published.Fence, published.Payload,
            new Lazy<CachedScoreboardProjection>(Decode, LazyThreadSafetyMode.ExecutionAndPublication));
        var entry = slot.GetOrInstall(candidate);
        NoCtfTelemetry.RecordLeaderboardDecodedCache(!ReferenceEquals(entry, candidate), published.Payload.Length);
        return ValueTask.FromResult(entry.Projection.Value);

        CachedScoreboardProjection Decode()
        {
            using var measurement = NoCtfTelemetry.MeasureLeaderboardRead(LeaderboardReadPerformanceStage.Decode);
            return Deserialize(competitionId, published);
        }
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

    public void Dispose() => snapshots.Dispose();

    private sealed record Entry(long Fence, string Payload, Lazy<CachedScoreboardProjection> Projection);

    private sealed class PublicationSlot(long capacity)
    {
        private Entry? current;
        public long Capacity { get; } = capacity;
        public Entry? Read() => Volatile.Read(ref current);

        public Entry GetOrInstall(Entry candidate)
        {
            while (true)
            {
                var previous = Read();
                if (previous is not null && previous.Fence == candidate.Fence && previous.Payload == candidate.Payload)
                    return previous;
                if (previous is not null && previous.Fence > candidate.Fence) return candidate;
                if (ReferenceEquals(Interlocked.CompareExchange(ref current, candidate, previous), previous)) return candidate;
            }
        }
    }
}
