using StackExchange.Redis;

namespace NoCTF.Infrastructure.Runtime.Capacity;

public sealed class RuntimeDispatchWakeupGate(IConnectionMultiplexer? redis = null)
{
    public async Task<bool> TryBeginAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return redis is null || await redis.GetDatabase().StringSetAsync("runtime-dispatch:wakeup", "1",
            TimeSpan.FromMilliseconds(500), When.NotExists);
    }
}
