using System.Text.Json;
using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Net;

namespace NoCTF.Infrastructure.Messaging;

public interface IClusterSchedulerStatusStore
{
    Task TakeOverAsync(ClusterSchedulerStatus status, CancellationToken cancellationToken);
    Task RenewAsync(ClusterSchedulerStatus status, CancellationToken cancellationToken);
    Task ReleaseAsync(ClusterSchedulerStatus status, CancellationToken cancellationToken);
    Task<ClusterSchedulerStatus?> ReadAsync(CancellationToken cancellationToken);
}

public sealed record ClusterSchedulerStatus(string OwnerNode, DateTimeOffset TakenOverAt);

public sealed class NatsClusterSchedulerStatusStore(INatsConnection connection)
    : IClusterSchedulerStatusStore
{
    private const string Bucket = "NOCTF_SCHEDULER_STATUS_V3";
    private const string Key = "active";
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(15);
    private ulong ownedRevision;

    public async Task TakeOverAsync(
        ClusterSchedulerStatus status, CancellationToken cancellationToken)
    {
        var store = await OpenAsync(cancellationToken);
        ownedRevision = await store.PutAsync(Key, Serialize(status),
            cancellationToken: cancellationToken);
    }

    public async Task RenewAsync(
        ClusterSchedulerStatus status, CancellationToken cancellationToken)
    {
        var revision = ownedRevision;
        if (revision == 0)
            throw new InvalidOperationException("The scheduler diagnostic lease is not owned.");
        var store = await OpenAsync(cancellationToken);
        ownedRevision = await store.UpdateAsync(Key, Serialize(status), revision,
            cancellationToken: cancellationToken);
    }

    public async Task ReleaseAsync(
        ClusterSchedulerStatus status, CancellationToken cancellationToken)
    {
        var revision = ownedRevision;
        ownedRevision = 0;
        if (revision == 0) return;
        var store = await OpenAsync(cancellationToken);
        try
        {
            await store.DeleteAsync(Key,
                new NatsKVDeleteOpts { Revision = revision }, cancellationToken);
        }
        catch (NatsKVException)
        {
            // A new scheduler already owns the diagnostic entry.
        }
    }

    public async Task<ClusterSchedulerStatus?> ReadAsync(CancellationToken cancellationToken)
    {
        var store = await OpenAsync(cancellationToken);
        try
        {
            var entry = await store.GetEntryAsync<byte[]>(Key,
                cancellationToken: cancellationToken);
            return entry.Value is null
                ? null
                : JsonSerializer.Deserialize(entry.Value,
                    ClusterSchedulerStatusJsonContext.Default.ClusterSchedulerStatus);
        }
        catch (NatsKVKeyNotFoundException)
        {
            return null;
        }
        catch (NatsKVKeyDeletedException)
        {
            return null;
        }
    }

    private async Task<INatsKVStore> OpenAsync(CancellationToken ct) =>
        await connection.CreateKeyValueStoreContext().CreateOrUpdateStoreAsync(
            new NatsKVConfig(Bucket)
            {
                Description = "NoCTF scheduler health status",
                History = 1,
                MaxAge = Lifetime
            }, ct);

    private static byte[] Serialize(ClusterSchedulerStatus status) =>
        JsonSerializer.SerializeToUtf8Bytes(status,
            ClusterSchedulerStatusJsonContext.Default.ClusterSchedulerStatus);
}

[JsonSerializable(typeof(ClusterSchedulerStatus))]
internal partial class ClusterSchedulerStatusJsonContext : JsonSerializerContext;
