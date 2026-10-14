using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Net;
using NoCTF.Application.Observability;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Messaging;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed record LeaderboardFencedPayload(long Fence, string Payload);

public interface ILeaderboardPublicationFence
{
    Task<long> IssueAsync(Guid competitionId, long minimumFence, CancellationToken cancellationToken);
    Task<bool> TryCommitAsync(Guid competitionId, long fence, string payload, CancellationToken cancellationToken);
    Task<LeaderboardFencedPayload?> GetAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<bool> IsCurrentAsync(Guid competitionId, long fence, CancellationToken cancellationToken);
    Task InvalidateAsync(Guid competitionId, long minimumFence, CancellationToken cancellationToken);
}

/// <summary>NATS fences publication; FusionCache owns immutable snapshot payloads.</summary>
public sealed class NatsLeaderboardPublicationFence(
    INatsConnection connection, IFusionCacheProvider caches) : ILeaderboardPublicationFence
{
    private const string Bucket = "NOCTF_SCOREBOARD_PUBLICATIONS_V3";
    private readonly NatsKeyValueStoreAccess storeAccess = new(ct =>
        connection.CreateKeyValueStoreContext().CreateOrUpdateStoreAsync(
            new NatsKVConfig(Bucket)
            {
                Description = "NoCTF scoreboard publication versions",
                History = 1
            }, ct));
    private readonly IFusionCache cache = caches.GetCache(NoCtfCacheNames.Leaderboards);
    private static readonly FusionCacheEntryOptions PayloadOptions = new()
    {
        Duration = TimeSpan.FromDays(1),
        IsFailSafeEnabled = false,
        AllowBackgroundDistributedCacheOperations = false,
        ReThrowDistributedCacheExceptions = true
    };

    public Task<long> IssueAsync(Guid competitionId, long minimumFence, CancellationToken ct) =>
        UpdateVersionAsync(competitionId, minimumFence, invalidate: false, ct);

    public async Task<bool> TryCommitAsync(
        Guid competitionId, long fence, string payload, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "success";
        try
        {
            await cache.SetAsync(PayloadKey(competitionId, fence), payload,
                PayloadOptions, token: ct);
            return await storeAccess.ExecuteAsync<bool>(async store =>
            {
                for (var attempt = 0; attempt < 16; attempt++)
                {
                    var entry = await ReadAsync(store, competitionId, ct);
                    if (entry is null || entry.Value.Value.IssuedVersion != fence)
                    {
                        outcome = "rejected";
                        // IssueAsync can advance IssuedVersion while retaining the current
                        // published payload. A duplicate commit must not remove that payload.
                        if (entry is { } rejected && rejected.Value.IssuedVersion > fence
                            && rejected.Value.PublishedVersion != fence)
                            await RetirePayloadAsync(competitionId, fence, ct);
                        return false;
                    }
                    if (entry.Value.Value.PublishedVersion == fence)
                        return true;
                    var next = entry.Value.Value with { PublishedVersion = fence };
                    try
                    {
                        await store.UpdateAsync(Key(competitionId), Serialize(next),
                            entry.Value.Revision, cancellationToken: ct);
                        if (entry.Value.Value.PublishedVersion is { } previous)
                            await RetirePayloadAsync(competitionId, previous, ct);
                        return true;
                    }
                    catch (NatsKVWrongLastRevisionException) when (attempt < 15)
                    {
                        await Task.Delay(Random.Shared.Next(1, 9), ct);
                    }
                }
                throw new InvalidOperationException("Scoreboard publication contention exceeded the retry limit.");
            }, ct);
        }
        catch
        {
            outcome = "failure";
            throw;
        }
        finally
        {
            NoCtfTelemetry.RecordNatsOperation("leaderboard_fence_commit", outcome,
                Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }

    public async Task<LeaderboardFencedPayload?> GetAsync(
        Guid competitionId, CancellationToken ct)
    {
        return await storeAccess.ExecuteAsync<LeaderboardFencedPayload?>(async store =>
        {
            var entry = await ReadAsync(store, competitionId, ct);
            if (entry?.Value.PublishedVersion is not long version)
                return null;
            var payload = await cache.GetOrDefaultAsync<string?>(
                PayloadKey(competitionId, version), null, token: ct);
            return payload is null ? null : new(version, payload);
        }, ct);
    }

    public async Task<bool> IsCurrentAsync(
        Guid competitionId, long fence, CancellationToken ct)
    {
        return await storeAccess.ExecuteAsync<bool>(async store =>
        {
            var entry = await ReadAsync(store, competitionId, ct);
            return entry?.Value.PublishedVersion == fence;
        }, ct);
    }

    public async Task InvalidateAsync(
        Guid competitionId, long minimumFence, CancellationToken ct) =>
        _ = await UpdateVersionAsync(competitionId, minimumFence, invalidate: true, ct);

    private async Task<long> UpdateVersionAsync(
        Guid competitionId, long minimumFence, bool invalidate, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "success";
        try
        {
            return await storeAccess.ExecuteAsync<long>(async store =>
            {
                for (var attempt = 0; attempt < 16; attempt++)
                {
                    var current = await ReadAsync(store, competitionId, ct);
                    var previousVersion = current?.Value.IssuedVersion ?? 0;
                    var version = Math.Max(minimumFence,
                        checked(previousVersion + 1));
                    var next = new LeaderboardPublicationState(version,
                        invalidate ? null : current?.Value.PublishedVersion);
                    try
                    {
                        if (current is null)
                            await store.CreateAsync(Key(competitionId), Serialize(next),
                                cancellationToken: ct);
                        else
                            await store.UpdateAsync(Key(competitionId), Serialize(next),
                                current.Value.Revision, cancellationToken: ct);
                        if (invalidate && current?.Value.PublishedVersion is { } previous)
                            await RetirePayloadAsync(competitionId, previous, ct);
                        return version;
                    }
                    catch (NatsKVException exception) when (attempt < 15
                        && exception is NatsKVCreateException or NatsKVWrongLastRevisionException)
                    {
                        await Task.Delay(Random.Shared.Next(1, 9), ct);
                    }
                }
                throw new InvalidOperationException("Scoreboard version contention exceeded the retry limit.");
            }, ct);
        }
        catch
        {
            outcome = "failure";
            throw;
        }
        finally
        {
            NoCtfTelemetry.RecordNatsOperation(
                invalidate ? "leaderboard_fence_invalidate" : "leaderboard_fence_issue",
                outcome, Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }

    private async Task RetirePayloadAsync(Guid competitionId, long fence, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "success";
        try
        {
            await cache.RemoveAsync(PayloadKey(competitionId, fence), PayloadOptions, ct);
        }
        catch (Exception)
        {
            // Publication/invalidation already committed. Keep its result authoritative;
            // the immutable entry's TTL bounds retention if best-effort cleanup fails.
            outcome = "failure";
        }
        finally
        {
            NoCtfTelemetry.RecordNatsOperation("leaderboard_payload_retire", outcome,
                Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }

    private static async Task<(LeaderboardPublicationState Value, ulong Revision)?> ReadAsync(
        INatsKVStore store, Guid competitionId, CancellationToken ct)
    {
        try
        {
            var entry = await store.GetEntryAsync<byte[]>(Key(competitionId),
                cancellationToken: ct);
            var state = entry.Value is null ? null : JsonSerializer.Deserialize(
                entry.Value, LeaderboardPublicationJsonContext.Default.LeaderboardPublicationState);
            return state is null
                ? throw new InvalidOperationException("Invalid scoreboard publication state.")
                : (state, entry.Revision);
        }
        catch (NatsKVKeyNotFoundException) { return null; }
        catch (NatsKVKeyDeletedException) { return null; }
    }

    private static string Key(Guid competitionId) => $"competition.{competitionId:N}";
    internal static string PayloadKey(Guid competitionId, long fence) =>
        $"scoreboard:published:{competitionId:N}:{fence}";
    private static byte[] Serialize(LeaderboardPublicationState state) =>
        JsonSerializer.SerializeToUtf8Bytes(state,
            LeaderboardPublicationJsonContext.Default.LeaderboardPublicationState);
}

internal sealed record LeaderboardPublicationState(long IssuedVersion, long? PublishedVersion);

[JsonSerializable(typeof(LeaderboardPublicationState))]
internal partial class LeaderboardPublicationJsonContext : JsonSerializerContext;
