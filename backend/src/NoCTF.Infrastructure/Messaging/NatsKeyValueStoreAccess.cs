using NATS.Client.Core;
using NATS.Client.KeyValueStore;

namespace NoCTF.Infrastructure.Messaging;

/// <summary>
/// Initializes one process-owned KV handle without running stream management requests
/// for every operation. The connection itself handles ordinary reconnects.
/// </summary>
internal sealed class NatsKeyValueStoreAccess(
    Func<CancellationToken, ValueTask<INatsKVStore>> open)
{
    private readonly SemaphoreSlim initialization = new(1, 1);
    private INatsKVStore? current;

    public async Task<T> ExecuteAsync<T>(
        Func<INatsKVStore, Task<T>> operation, CancellationToken cancellationToken)
    {
        var store = await OpenAsync(cancellationToken);
        try
        {
            return await operation(store);
        }
        catch (NatsException exception) when (exception is not NatsKVException)
        {
            // A deleted stream or transport failure must remain visible to the caller.
            // Reopen on the next operation, never replay an operation with an unknown outcome.
            Interlocked.CompareExchange(ref current, null, store);
            throw;
        }
    }

    public async Task ExecuteAsync(
        Func<INatsKVStore, Task> operation, CancellationToken cancellationToken) =>
        await ExecuteAsync(async store =>
        {
            await operation(store);
            return true;
        }, cancellationToken);

    private async ValueTask<INatsKVStore> OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref current) is { } available)
            return available;

        await initialization.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref current) is { } initialized)
                return initialized;
            var store = await open(cancellationToken);
            Volatile.Write(ref current, store);
            return store;
        }
        finally
        {
            // Failed/cancelled initialization is not cached; another caller can retry.
            initialization.Release();
        }
    }
}
