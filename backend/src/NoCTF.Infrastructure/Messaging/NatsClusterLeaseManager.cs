using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Net;

namespace NoCTF.Infrastructure.Messaging;

public interface IClusterLeaseManager
{
    Task<IClusterLease?> TryAcquireAsync(
        string key,
        string owner,
        CancellationToken cancellationToken);
}

public interface IClusterLease : IAsyncDisposable
{
    ulong FencingToken { get; }
    Task RenewAsync(CancellationToken cancellationToken);
}

public sealed class NatsClusterLeaseManager(
    INatsConnection connection,
    TimeProvider? configuredClock = null) : IClusterLeaseManager
{
    private readonly TimeProvider clock = configuredClock ?? TimeProvider.System;
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan RenewalInterval = TimeSpan.FromSeconds(10);
    private const string Bucket = "NOCTF_LEASES_V2";

    async Task<IClusterLease?> IClusterLeaseManager.TryAcquireAsync(
        string key,
        string owner,
        CancellationToken cancellationToken) =>
        await TryAcquireAsync(key, owner, cancellationToken);

    public async Task<NatsClusterLease?> TryAcquireAsync(
        string key,
        string owner,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        var context = connection.CreateKeyValueStoreContext();
        var store = await context.CreateOrUpdateStoreAsync(new NatsKVConfig(Bucket)
        {
            Description = "NoCTF process ownership leases",
            History = 1,
            MaxAge = LeaseDuration
        }, cancellationToken);
        try
        {
            var revision = await store.CreateAsync(
                key,
                LeaseValue(owner, clock.GetUtcNow()),
                cancellationToken: cancellationToken);
            return new(store, key, owner, revision, clock);
        }
        catch (NatsKVException)
        {
            return null;
        }
    }

    internal static string LeaseValue(string owner, DateTimeOffset renewedAt) =>
        owner + "|" + renewedAt.UtcTicks.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class NatsClusterLease(
    INatsKVStore store,
    string key,
    string owner,
    ulong revision,
    TimeProvider clock) : IClusterLease
{
    public ulong FencingToken { get; private set; } = revision;

    public async Task RenewAsync(CancellationToken cancellationToken)
    {
        FencingToken = await store.UpdateAsync(
            key,
            NatsClusterLeaseManager.LeaseValue(owner, clock.GetUtcNow()),
            FencingToken,
            cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await store.DeleteAsync(key, new NatsKVDeleteOpts { Revision = FencingToken });
        }
        catch (NatsKVException)
        {
            // A newer owner already replaced or expired this lease.
        }
    }
}
