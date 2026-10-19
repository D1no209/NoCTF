using System.Text.Json;
using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Net;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Infrastructure.GameplayFacts.Processing;

public sealed record GameplayFactRecoveryCursor(DateTimeOffset UpdatedAt, Guid Id);

public sealed class GameplayFactRecoveryCursorStore(INatsConnection connection)
{
    private readonly NatsKeyValueStoreAccess access = new(ct => connection.CreateKeyValueStoreContext()
        .CreateOrUpdateStoreAsync(new NatsKVConfig("NOCTF_GAMEPLAY_RECOVERY_V1") { History = 1 }, ct));

    public Task<(GameplayFactRecoveryCursor? Cursor, ulong? Revision)> ReadAsync(CancellationToken ct) =>
        access.ExecuteAsync<(GameplayFactRecoveryCursor?, ulong?)>(async store =>
        {
            try
            {
                var entry = await store.GetEntryAsync<byte[]>("queued", cancellationToken: ct);
                return (JsonSerializer.Deserialize(entry.Value!, GameplayFactRecoveryJsonContext.Default.GameplayFactRecoveryCursor), entry.Revision);
            }
            catch (NatsKVKeyNotFoundException) { return (null, null); }
            catch (NatsKVKeyDeletedException) { return (null, null); }
        }, ct);

    public Task<bool> SaveAsync(GameplayFactRecoveryCursor? cursor, ulong? revision, CancellationToken ct) =>
        access.ExecuteAsync(async store =>
        {
            var data = JsonSerializer.SerializeToUtf8Bytes(cursor, GameplayFactRecoveryJsonContext.Default.GameplayFactRecoveryCursor);
            try
            {
                if (revision is null) await store.CreateAsync("queued", data, cancellationToken: ct);
                else await store.UpdateAsync("queued", data, revision.Value, cancellationToken: ct);
                return true;
            }
            catch (NatsKVException exception) when (exception is NatsKVCreateException or NatsKVWrongLastRevisionException)
            {
                return false;
            }
        }, ct);
}

[JsonSerializable(typeof(GameplayFactRecoveryCursor))]
internal partial class GameplayFactRecoveryJsonContext : JsonSerializerContext;
