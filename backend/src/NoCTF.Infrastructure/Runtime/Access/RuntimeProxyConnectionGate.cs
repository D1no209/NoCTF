using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Net;
using NoCTF.Application.Runtime.Access;

namespace NoCTF.Infrastructure.Runtime.Access;

public sealed class RuntimeProxyConnectionGate(
    RuntimeProxyOptions options,
    INatsConnection? connection = null,
    TimeProvider? configuredClock = null) : IRuntimeProxyConnectionGate
{
    private readonly ConcurrentDictionary<Guid, int> localCounts = new();
    private readonly TimeProvider clock = configuredClock ?? TimeProvider.System;
    private readonly TimeSpan lifetime = TimeSpan.FromMinutes(options.MaximumConnectionMinutes)
        .Add(TimeSpan.FromMinutes(2));
    private const string Bucket = "NOCTF_PROXY_LEASES_V3";

    public async Task<IRuntimeProxyConnectionLease?> TryAcquireAsync(
        Guid runtimeInstanceId, CancellationToken cancellationToken)
    {
        if (connection is null)
            return TryAcquireLocal(runtimeInstanceId);
        var store = await OpenAsync(cancellationToken);
        var key = runtimeInstanceId.ToString("N");
        var leaseId = Guid.NewGuid();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var now = clock.GetUtcNow();
            NatsKVEntry<byte[]>? current;
            try
            {
                current = await store.GetEntryAsync<byte[]>(key,
                    cancellationToken: cancellationToken);
            }
            catch (NatsKVKeyNotFoundException) { current = null; }
            catch (NatsKVKeyDeletedException) { current = null; }
            var leases = current?.Value is { } bytes
                ? JsonSerializer.Deserialize(bytes,
                    RuntimeProxyLeaseJsonContext.Default.ProxyLeaseSet)?.Leases
                    ?? throw new InvalidOperationException("Invalid Runtime proxy lease state.")
                : [];
            var active = leases.Where(item => item.ExpiresAt > now).ToList();
            if (active.Count >= options.MaximumConnectionsPerRuntime)
                return null;
            active.Add(new ProxyLease(leaseId, now.Add(lifetime)));
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                new ProxyLeaseSet([.. active]),
                RuntimeProxyLeaseJsonContext.Default.ProxyLeaseSet);
            try
            {
                if (current is null)
                    await store.CreateAsync(key, payload,
                        cancellationToken: cancellationToken);
                else
                    await store.UpdateAsync(key, payload, current.Value.Revision,
                        cancellationToken: cancellationToken);
                return new DistributedLease(store, key, leaseId, clock);
            }
            catch (NatsKVException) when (attempt < 7)
            {
                await Task.Delay(Random.Shared.Next(1, 9), cancellationToken);
            }
        }
        throw new InvalidOperationException("Runtime proxy lease contention exceeded the retry limit.");
    }

    private async Task<INatsKVStore> OpenAsync(CancellationToken ct) =>
        await connection!.CreateKeyValueStoreContext().CreateOrUpdateStoreAsync(
            new NatsKVConfig(Bucket)
            {
                Description = "NoCTF Runtime proxy connection leases",
                History = 1,
                MaxAge = lifetime
            }, ct);

    private IRuntimeProxyConnectionLease? TryAcquireLocal(Guid runtimeInstanceId)
    {
        while (true)
        {
            var current = localCounts.GetOrAdd(runtimeInstanceId, 0);
            if (current >= options.MaximumConnectionsPerRuntime)
                return null;
            if (localCounts.TryUpdate(runtimeInstanceId, current + 1, current))
                return new LocalLease(localCounts, runtimeInstanceId);
        }
    }

    private sealed class LocalLease(
        ConcurrentDictionary<Guid, int> counts, Guid runtimeInstanceId)
        : IRuntimeProxyConnectionLease
    {
        private int disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                var remaining = counts.AddOrUpdate(runtimeInstanceId, 0,
                    static (_, value) => Math.Max(0, value - 1));
                if (remaining == 0)
                    counts.TryRemove(new KeyValuePair<Guid, int>(runtimeInstanceId, 0));
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DistributedLease(
        INatsKVStore store, string key, Guid leaseId, TimeProvider clock)
        : IRuntimeProxyConnectionLease
    {
        private int disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                NatsKVEntry<byte[]> current;
                try { current = await store.GetEntryAsync<byte[]>(key); }
                catch (NatsKVException) { return; }
                if (current.Value is null) return;
                var state = JsonSerializer.Deserialize(current.Value,
                    RuntimeProxyLeaseJsonContext.Default.ProxyLeaseSet);
                if (state is null) return;
                var remaining = state.Leases.Where(item =>
                    item.Id != leaseId && item.ExpiresAt > clock.GetUtcNow()).ToArray();
                if (remaining.Length == state.Leases.Length) return;
                try
                {
                    if (remaining.Length == 0)
                        await store.DeleteAsync(key,
                            new NatsKVDeleteOpts { Revision = current.Revision });
                    else
                        await store.UpdateAsync(key,
                            JsonSerializer.SerializeToUtf8Bytes(
                                new ProxyLeaseSet(remaining),
                                RuntimeProxyLeaseJsonContext.Default.ProxyLeaseSet),
                            current.Revision);
                    return;
                }
                catch (NatsKVException) when (attempt < 7)
                {
                    await Task.Delay(Random.Shared.Next(1, 9));
                }
            }
        }
    }
}

internal sealed record ProxyLease(Guid Id, DateTimeOffset ExpiresAt);
internal sealed record ProxyLeaseSet(ProxyLease[] Leases);

[JsonSerializable(typeof(ProxyLeaseSet))]
internal partial class RuntimeProxyLeaseJsonContext : JsonSerializerContext;
