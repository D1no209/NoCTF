using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Access;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Runtime.Access;

public sealed class RuntimeProxyTargetReader(
    NoCtfDbContext db) : IRuntimeProxyTargetReader
{
    public async Task<RuntimeProxyTarget?> FindAsync(
        Guid runtimeInstanceId,
        int bindingIndex,
        CancellationToken cancellationToken)
    {
        var runtime = await db.RuntimeInstances.AsNoTracking()
            .Where(runtime => runtime.Id == runtimeInstanceId
                && runtime.State == RuntimeState.Running
                && (runtime.AccessMode == RuntimeAccessMode.DirectAndWsrx
                    || runtime.AccessMode == RuntimeAccessMode.WsrxOnly))
            .SingleOrDefaultAsync(cancellationToken);
        var endpoint = runtime?.AccessEndpoints.SingleOrDefault(candidate =>
            candidate.BindingIndex == bindingIndex
            && !string.IsNullOrWhiteSpace(candidate.TargetHost)
            && candidate.TargetPort is >= 1 and <= 65535);
        return runtime is null || endpoint is null
            ? null
            : new RuntimeProxyTarget(
                runtime.Id,
                endpoint.BindingIndex,
                endpoint.TargetHost!,
                endpoint.TargetPort!.Value,
                runtime.CompetitionId,
                runtime.CompetitionChallengeId,
                runtime.TeamId,
                runtime.TrafficCaptureEnabled,
                runtime.TrafficCaptureLimitBytes);
    }
}

public sealed class RuntimeProxyConnectionGate(
    RuntimeProxyOptions options,
    IConnectionMultiplexer? redis = null) : IRuntimeProxyConnectionGate
{
    private const string AcquireScript = """
        local value = redis.call('INCR', KEYS[1])
        if value > tonumber(ARGV[1]) then
          redis.call('DECR', KEYS[1])
          return 0
        end
        redis.call('PEXPIRE', KEYS[1], ARGV[2])
        return 1
        """;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int>
        localCounts = new();

    public async Task<IRuntimeProxyConnectionLease?> TryAcquireAsync(
        Guid runtimeInstanceId,
        CancellationToken cancellationToken)
    {
        if (redis is null)
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

        var key = (RedisKey)$"noctf:runtime-proxy:connections:{runtimeInstanceId:N}";
        var ttl = TimeSpan.FromMinutes(options.MaximumConnectionMinutes).Add(TimeSpan.FromMinutes(2));
        var acquired = (long)await redis.GetDatabase().ScriptEvaluateAsync(
            AcquireScript,
            [key],
            [options.MaximumConnectionsPerRuntime, checked((long)ttl.TotalMilliseconds)]);
        cancellationToken.ThrowIfCancellationRequested();
        return acquired == 1
            ? new RedisLease(redis.GetDatabase(), key)
            : null;
    }

    private sealed class LocalLease(
        System.Collections.Concurrent.ConcurrentDictionary<Guid, int> counts,
        Guid runtimeInstanceId) : IRuntimeProxyConnectionLease
    {
        private int disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                var remaining = counts.AddOrUpdate(
                    runtimeInstanceId,
                    0,
                    static (_, value) => Math.Max(0, value - 1));
                if (remaining == 0)
                {
                    counts.TryRemove(new KeyValuePair<Guid, int>(
                        runtimeInstanceId,
                        0));
                }
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RedisLease(IDatabase database, RedisKey key)
        : IRuntimeProxyConnectionLease
    {
        private int disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            const string releaseScript = """
                local value = tonumber(redis.call('GET', KEYS[1]) or '0')
                if value <= 1 then
                  redis.call('DEL', KEYS[1])
                else
                  redis.call('DECR', KEYS[1])
                end
                return 1
                """;
            _ = await database.ScriptEvaluateAsync(releaseScript, [key]);
        }
    }
}
