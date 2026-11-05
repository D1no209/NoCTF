using System.Text.Json;
using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Net;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Infrastructure.GameplayFacts.Processing;

public sealed record GameplayFactRecoveryCursor(DateTimeOffset UpdatedAt, Guid Id);
public enum GameplayFactRecoveryArea { General, FixExecutions }

public sealed class GameplayFactRecoveryCursorStore(INatsConnection connection)
{
    private readonly NatsKeyValueStoreAccess access = new(ct => connection.CreateKeyValueStoreContext()
        .CreateOrUpdateStoreAsync(new NatsKVConfig("NOCTF_GAMEPLAY_RECOVERY_V1") { History = 1 }, ct));

    public Task<(GameplayFactRecoveryCursor? Cursor, ulong? Revision)> ReadAsync(CancellationToken ct,
        GameplayFactRecoveryArea area = GameplayFactRecoveryArea.General) =>
        access.ExecuteAsync<(GameplayFactRecoveryCursor?, ulong?)>(async store =>
        {
            try
            {
                var entry = await store.GetEntryAsync<byte[]>(Key(area), cancellationToken: ct);
                return (JsonSerializer.Deserialize(entry.Value!, GameplayFactRecoveryJsonContext.Default.GameplayFactRecoveryCursor), entry.Revision);
            }
            catch (NatsKVKeyNotFoundException) { return (null, null); }
            catch (NatsKVKeyDeletedException) { return (null, null); }
        }, ct);

    public Task<bool> SaveAsync(GameplayFactRecoveryCursor? cursor, ulong? revision, CancellationToken ct,
        GameplayFactRecoveryArea area = GameplayFactRecoveryArea.General) =>
        access.ExecuteAsync(async store =>
        {
            var data = JsonSerializer.SerializeToUtf8Bytes(cursor, GameplayFactRecoveryJsonContext.Default.GameplayFactRecoveryCursor);
            try
            {
                if (revision is null) await store.CreateAsync(Key(area), data, cancellationToken: ct);
                else await store.UpdateAsync(Key(area), data, revision.Value, cancellationToken: ct);
                return true;
            }
            catch (NatsKVException exception) when (exception is NatsKVCreateException or NatsKVWrongLastRevisionException)
            {
                return false;
            }
        }, ct);

    private static string Key(GameplayFactRecoveryArea area) => area switch
    {
        GameplayFactRecoveryArea.General => "queued",
        GameplayFactRecoveryArea.FixExecutions => "fix",
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, null)
    };
}

[JsonSerializable(typeof(GameplayFactRecoveryCursor))]
internal partial class GameplayFactRecoveryJsonContext : JsonSerializerContext;
