using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Net;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Infrastructure.Runtime.Capacity;

public sealed class RuntimeDispatchWakeupGate(INatsConnection? connection = null)
{
    private readonly NatsKeyValueStoreAccess? storeAccess = connection is null ? null : new(ct =>
        connection.CreateKeyValueStoreContext().CreateOrUpdateStoreAsync(
            new NatsKVConfig("NOCTF_RUNTIME_WAKEUP_V3")
            {
                Description = "NoCTF Runtime dispatch coalescing",
                History = 1,
                MaxAge = TimeSpan.FromMilliseconds(500)
            }, ct));

    public async Task<bool> TryBeginAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (storeAccess is null) return true;
        return await storeAccess.ExecuteAsync<bool>(async store =>
        {
            try
            {
                await store.CreateAsync("dispatch", 1, cancellationToken: ct);
                return true;
            }
            catch (NatsKVCreateException)
            {
                return false;
            }
        }, ct);
    }
}
