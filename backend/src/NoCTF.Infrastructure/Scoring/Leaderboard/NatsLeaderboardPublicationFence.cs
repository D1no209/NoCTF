using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Net;
using NoCTF.Application.Observability;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Messaging;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed record LeaderboardFencedPayload(long Fence, string Payload);
public sealed record LeaderboardBuildTicket(long Version, long RequestedRevision, long AccessRevision);

public interface ILeaderboardPublicationFence
{
    Task<long> IssueAsync(Guid competitionId, long minimumFence, CancellationToken cancellationToken);
    Task<bool> TryCommitAsync(Guid competitionId, long fence, string payload, CancellationToken cancellationToken);
    Task<LeaderboardFencedPayload?> GetAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<bool> IsCurrentAsync(Guid competitionId, long fence, CancellationToken cancellationToken);
    Task InvalidateAsync(Guid competitionId, long minimumFence, CancellationToken cancellationToken);
    async Task<LeaderboardBuildTicket> BeginBuildAsync(Guid competitionId, long minimumVersion, CancellationToken ct) =>
        new(await IssueAsync(competitionId, minimumVersion, ct), 0, 0);
    Task<bool> TryPublishAsync(Guid competitionId, LeaderboardBuildTicket ticket, long sourceProgress, string payload, CancellationToken ct) =>
        TryCommitAsync(competitionId, ticket.Version, payload, ct);
    Task<bool> HasPendingAsync(Guid competitionId, CancellationToken ct) => Task.FromResult(true);
    Task InvalidateAccessAsync(Guid competitionId, long minimumVersion, CancellationToken ct) => InvalidateAsync(competitionId, minimumVersion, ct);
    Task CleanupAsync(CancellationToken ct) => Task.CompletedTask;
    Task<IReadOnlyList<Guid>> GetPendingCompetitionsAsync(CancellationToken ct);
}

public sealed class NatsLeaderboardPublicationFence(INatsConnection connection, IFusionCacheProvider caches)
    : ILeaderboardPublicationFence
{
    private readonly NatsKeyValueStoreAccess storeAccess = new(ct =>
        connection.CreateKeyValueStoreContext().CreateOrUpdateStoreAsync(
            new NatsKVConfig("NOCTF_SCOREBOARD_PUBLICATIONS_V4") { History = 1 }, ct));
    private readonly IFusionCache cache = caches.GetCache(NoCtfCacheNames.Leaderboards);
    private static readonly FusionCacheEntryOptions PayloadOptions = new FusionCacheEntryOptions
    {
        IsFailSafeEnabled = false,
        AllowBackgroundDistributedCacheOperations = false,
        ReThrowDistributedCacheExceptions = true
    }.SetDurationInfinite().SetDistributedCacheDurationInfinite();

    public async Task<long> IssueAsync(Guid competitionId, long minimumFence, CancellationToken ct) =>
        (await BeginBuildAsync(competitionId, minimumFence, ct)).Version;

    public Task<LeaderboardBuildTicket> BeginBuildAsync(Guid competitionId, long minimumVersion, CancellationToken ct) =>
        storeAccess.ExecuteAsync<LeaderboardBuildTicket>(async store =>
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var entry = await ReadAsync(store, competitionId, ct);
                var state = entry?.Value ?? new();
                var version = Math.Max(minimumVersion, checked(state.IssuedVersion + 1));
                if (await UpdateAsync(store, competitionId, state with { IssuedVersion = version, IssuedRevision = state.RequestedRevision }, entry?.Revision, ct))
                    return new(version, state.RequestedRevision, state.AccessRevision);
            }
            throw new LeaderboardPublicationConcurrencyException(LeaderboardPublicationStage.BeginBuild);
        }, ct);

    public Task<bool> TryCommitAsync(Guid competitionId, long fence, string payload, CancellationToken ct) =>
        TryPublishAsync(competitionId, new(fence, 0, 0), 0, payload, ct);

    public async Task<bool> TryPublishAsync(Guid competitionId, LeaderboardBuildTicket ticket,
        long sourceProgress, string payload, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "rejected";
        var payloadKey = PayloadKey(competitionId, ticket.Version, payload);
        try
        {
            await RegisterCandidateAsync(new(competitionId, ticket.Version, payloadKey, DateTimeOffset.UtcNow), ct);
            await cache.SetAsync(payloadKey, payload, PayloadOptions, token: ct);
            var accepted = await storeAccess.ExecuteAsync<bool>(async store =>
            {
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    var entry = await ReadAsync(store, competitionId, ct);
                    if (entry is null) return false;
                    var current = entry.Value.Value;
                    if (ticket.AccessRevision != current.AccessRevision) return false;
                    if (current.PublishedVersion == ticket.Version)
                        return current.PublishedPayloadKey == payloadKey;
                    if (ticket.Version > current.IssuedVersion || ticket.Version <= (current.PublishedVersion ?? 0)
                        || ticket.AccessRevision != current.AccessRevision
                        || ticket.RequestedRevision < current.PublishedRevision
                        || sourceProgress < current.SourceProgress) return false;
                    var next = current with
                    {
                        PublishedVersion = ticket.Version, PublishedRevision = ticket.RequestedRevision,
                        PublishedPayloadKey = payloadKey, SourceProgress = sourceProgress
                    };
                    if (!await UpdateAsync(store, competitionId, next, entry.Value.Revision, ct)) continue;
                    if (current.PublishedPayloadKey is { } previous && previous != payloadKey)
                        await RetireAsync(competitionId, previous, ct);
                    return true;
                }
                throw new LeaderboardPublicationConcurrencyException(LeaderboardPublicationStage.Publication);
            }, ct);
            outcome = accepted ? "success" : "rejected";
            if (!accepted) await RetireAsync(competitionId, payloadKey, ct);
            return accepted;
        }
        catch { outcome = "failure"; throw; }
        finally { NoCtfTelemetry.RecordNatsOperation("leaderboard_fence_commit", outcome, Stopwatch.GetElapsedTime(started).TotalSeconds); }
    }

    public Task<LeaderboardFencedPayload?> GetAsync(Guid competitionId, CancellationToken ct) =>
        storeAccess.ExecuteAsync<LeaderboardFencedPayload?>(async store =>
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var entry = await ReadAsync(store, competitionId, ct);
                if (entry?.Value.PublishedVersion is not long version) return null;
                var key = entry.Value.Value.PublishedPayloadKey ?? throw new InvalidOperationException("Missing published payload key.");
                var payload = await cache.GetOrDefaultAsync<string?>(key, null, token: ct);
                if (payload is not null) return new(version, payload);
                if ((await ReadAsync(store, competitionId, ct))?.Value.PublishedVersion == version) return null;
            }
            return null;
        }, ct);

    public Task<bool> IsCurrentAsync(Guid competitionId, long fence, CancellationToken ct) =>
        storeAccess.ExecuteAsync<bool>(async store => (await ReadAsync(store, competitionId, ct))?.Value.PublishedVersion == fence, ct);

    public async Task<bool> HasPendingAsync(Guid competitionId, CancellationToken ct)
    {
        var state = await storeAccess.ExecuteAsync<LeaderboardPublicationState?>(async store =>
            (await ReadAsync(store, competitionId, ct))?.Value, ct);
        return state is null || state.PublishedVersion is null || state.RequestedRevision > state.PublishedRevision
            || await GetAsync(competitionId, ct) is null;
    }

    public Task InvalidateAsync(Guid competitionId, long minimumFence, CancellationToken ct) => MarkAsync(competitionId, false, ct);
    public Task InvalidateAccessAsync(Guid competitionId, long minimumVersion, CancellationToken ct) => MarkAsync(competitionId, true, ct);

    public Task<IReadOnlyList<Guid>> GetPendingCompetitionsAsync(CancellationToken ct) => storeAccess.ExecuteAsync<IReadOnlyList<Guid>>(async store =>
    {
        var pending = new List<Guid>();
        await foreach (var key in store.GetKeysAsync(cancellationToken: ct))
        {
            if (!key.StartsWith("competition.", StringComparison.Ordinal)) continue;
            var id = Guid.ParseExact(key["competition.".Length..], "N");
            var state = (await ReadAsync(store, id, ct))?.Value;
            if (state is not null && (state.PublishedVersion is null || state.RequestedRevision > state.PublishedRevision))
                pending.Add(id);
        }
        return pending;
    }, ct);

    private async Task MarkAsync(Guid competitionId, bool accessChanged, CancellationToken ct)
    {
        await storeAccess.ExecuteAsync<bool>(async store =>
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var entry = await ReadAsync(store, competitionId, ct);
                var state = entry?.Value ?? new();
                if (!accessChanged && state.RequestedRevision > state.IssuedRevision) return true;
                var next = state with
                {
                    RequestedRevision = checked(state.RequestedRevision + 1),
                    AccessRevision = accessChanged ? checked(state.AccessRevision + 1) : state.AccessRevision
                };
                if (await UpdateAsync(store, competitionId, next, entry?.Revision, ct)) return true;
            }
            throw new LeaderboardPublicationConcurrencyException(LeaderboardPublicationStage.Invalidation);
        }, ct);
    }

    private static async Task<bool> UpdateAsync(INatsKVStore store, Guid competitionId,
        LeaderboardPublicationState state, ulong? revision, CancellationToken ct)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(state, LeaderboardPublicationJsonContext.Default.LeaderboardPublicationState);
        try
        {
            if (revision is null) await store.CreateAsync(Key(competitionId), data, cancellationToken: ct);
            else await store.UpdateAsync(Key(competitionId), data, revision.Value, cancellationToken: ct);
            return true;
        }
        catch (NatsKVException exception) when (exception is NatsKVCreateException or NatsKVWrongLastRevisionException) { return false; }
    }

    private async Task RetireAsync(Guid competitionId, string key, CancellationToken ct)
    {
        try
        {
            var current = await storeAccess.ExecuteAsync<LeaderboardPublicationState?>(async store =>
                (await ReadAsync(store, competitionId, ct))?.Value, ct);
            if (current?.PublishedPayloadKey == key) return;
            await cache.RemoveAsync(key, PayloadOptions, ct);
            await storeAccess.ExecuteAsync(async store => { await store.DeleteAsync(CandidateKey(key), cancellationToken: ct); }, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // 已提交的快照仍然有效，候选登记供后台清理继续处理。
            NoCtfTelemetry.RecordNatsOperation("leaderboard_payload_retire", "failure", 0);
        }
    }

    private Task RegisterCandidateAsync(LeaderboardPayloadCandidate candidate, CancellationToken ct) =>
        storeAccess.ExecuteAsync(async store =>
        {
            try
            {
                await store.CreateAsync(CandidateKey(candidate.PayloadKey), JsonSerializer.SerializeToUtf8Bytes(candidate,
                    LeaderboardPublicationJsonContext.Default.LeaderboardPayloadCandidate), cancellationToken: ct);
            }
            catch (NatsKVCreateException) { }
        }, ct);

    public Task CleanupAsync(CancellationToken ct) => storeAccess.ExecuteAsync(async store =>
    {
        await foreach (var key in store.GetKeysAsync(cancellationToken: ct))
        {
            if (!key.StartsWith("candidate.", StringComparison.Ordinal)) continue;
            NatsKVEntry<byte[]> entry;
            try { entry = await store.GetEntryAsync<byte[]>(key, cancellationToken: ct); }
            catch (NatsKVKeyNotFoundException) { continue; }
            catch (NatsKVKeyDeletedException) { continue; }
            var candidate = entry.Value is null ? null : JsonSerializer.Deserialize(entry.Value,
                LeaderboardPublicationJsonContext.Default.LeaderboardPayloadCandidate);
            if (candidate is null) throw new InvalidOperationException("Invalid scoreboard candidate metadata.");
            if (DateTimeOffset.UtcNow - candidate.RegisteredAt < TimeSpan.FromMinutes(5)) continue;
            var current = await ReadAsync(store, candidate.CompetitionId, ct);
            if (current?.Value.PublishedPayloadKey == candidate.PayloadKey) continue;
            await cache.RemoveAsync(candidate.PayloadKey, PayloadOptions, ct);
            await store.DeleteAsync(key, cancellationToken: ct);
        }
    }, ct);

    private static string CandidateKey(string payloadKey) => "candidate." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadKey)));

    private static async Task<(LeaderboardPublicationState Value, ulong Revision)?> ReadAsync(INatsKVStore store, Guid competitionId, CancellationToken ct)
    {
        try
        {
            var entry = await store.GetEntryAsync<byte[]>(Key(competitionId), cancellationToken: ct);
            var state = entry.Value is null ? null : JsonSerializer.Deserialize(entry.Value, LeaderboardPublicationJsonContext.Default.LeaderboardPublicationState);
            return state is null ? throw new InvalidOperationException("Invalid scoreboard publication state.") : (state, entry.Revision);
        }
        catch (NatsKVKeyNotFoundException) { return null; }
        catch (NatsKVKeyDeletedException) { return null; }
    }

    private static string Key(Guid competitionId) => $"competition.{competitionId:N}";
    internal static string PayloadKey(Guid competitionId, long fence, string? payload = null) =>
        $"scoreboard:published:{competitionId:N}:{fence}" + (payload is null ? "" : ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))));
}

internal sealed record LeaderboardPublicationState(long IssuedVersion = 0, long? PublishedVersion = null,
    long RequestedRevision = 0, long PublishedRevision = 0, long AccessRevision = 0,
    long SourceProgress = 0, string? PublishedPayloadKey = null, long IssuedRevision = 0);
internal sealed record LeaderboardPayloadCandidate(Guid CompetitionId, long Version, string PayloadKey, DateTimeOffset RegisteredAt);

[JsonSerializable(typeof(LeaderboardPublicationState))]
[JsonSerializable(typeof(LeaderboardPayloadCandidate))]
internal partial class LeaderboardPublicationJsonContext : JsonSerializerContext;
