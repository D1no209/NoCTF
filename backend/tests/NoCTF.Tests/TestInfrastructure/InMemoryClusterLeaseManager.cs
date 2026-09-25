using System.Collections.Concurrent;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Tests;

internal sealed class InMemoryClusterLeaseManager : IClusterLeaseManager
{
    private readonly ConcurrentDictionary<string, string> owners = new(StringComparer.Ordinal);
    private long revision;

    public Task<IClusterLease?> TryAcquireAsync(
        string key,
        string owner,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!owners.TryAdd(key, owner))
            return Task.FromResult<IClusterLease?>(null);
        var fencingToken = checked((ulong)Interlocked.Increment(ref revision));
        return Task.FromResult<IClusterLease?>(new Lease(owners, key, owner, fencingToken));
    }

    private sealed class Lease(
        ConcurrentDictionary<string, string> owners,
        string key,
        string owner,
        ulong fencingToken) : IClusterLease
    {
        public ulong FencingToken { get; private set; } = fencingToken;

        public Task RenewAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!owners.TryGetValue(key, out var current)
                || !string.Equals(current, owner, StringComparison.Ordinal))
                throw new InvalidOperationException("The in-memory lease is no longer owned.");
            FencingToken++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            owners.TryRemove(new KeyValuePair<string, string>(key, owner));
            return ValueTask.CompletedTask;
        }
    }
}
