using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Net;

namespace NoCTF.Infrastructure.Runtime.Capacity;

public sealed class RuntimeDispatchWakeupGate(INatsConnection? connection = null)
{
    public async Task<bool> TryBeginAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (connection is null) return true;
        var store = await connection.CreateKeyValueStoreContext()
            .CreateOrUpdateStoreAsync(new NatsKVConfig("NOCTF_RUNTIME_WAKEUP_V3")
            {
                Description = "NoCTF Runtime dispatch coalescing",
                History = 1,
                MaxAge = TimeSpan.FromMilliseconds(500)
            }, ct);
        try
        {
            await store.CreateAsync("dispatch", 1, cancellationToken: ct);
            return true;
        }
        catch (NatsKVCreateException)
        {
            return false;
        }
    }
}
