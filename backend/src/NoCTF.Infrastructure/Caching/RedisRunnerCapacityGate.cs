using StackExchange.Redis;
using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Infrastructure.Caching;

public sealed class RedisRunnerCapacityGate(IConnectionMultiplexer redis) : IRunnerCapacityGate
{
    private const string ClaimScript = """
        if redis.call('EXISTS', KEYS[1]) == 0 then return 0 end
        local memory = tonumber(redis.call('HGET', KEYS[2], 'availableMemoryBytes') or '-1')
        local cpu = tonumber(redis.call('HGET', KEYS[2], 'availableNanoCpus') or '-1')
        local pids = tonumber(redis.call('HGET', KEYS[2], 'availablePids') or '-1')
        if memory < tonumber(ARGV[1]) or cpu < tonumber(ARGV[2]) or pids < tonumber(ARGV[3]) then return 0 end
        redis.call('HINCRBY', KEYS[2], 'availableMemoryBytes', -tonumber(ARGV[1]))
        redis.call('HINCRBY', KEYS[2], 'availableNanoCpus', -tonumber(ARGV[2]))
        redis.call('HINCRBY', KEYS[2], 'availablePids', -tonumber(ARGV[3]))
        redis.call('HSET', KEYS[3], 'runnerId', ARGV[4], 'memoryBytes', ARGV[1], 'nanoCpus', ARGV[2], 'pidsLimit', ARGV[3])
        return 1
        """;

    private const string ReleaseScript = """
        local memory = redis.call('HGET', KEYS[2], 'memoryBytes')
        local cpu = redis.call('HGET', KEYS[2], 'nanoCpus')
        local pids = redis.call('HGET', KEYS[2], 'pidsLimit')
        if not memory or not cpu or not pids then return 0 end
        redis.call('HINCRBY', KEYS[1], 'availableMemoryBytes', tonumber(memory))
        redis.call('HINCRBY', KEYS[1], 'availableNanoCpus', tonumber(cpu))
        redis.call('HINCRBY', KEYS[1], 'availablePids', tonumber(pids))
        redis.call('DEL', KEYS[2])
        return 1
        """;

    public async Task<RunnerCapacityClaim> TryClaimAsync(
        RunnerCapacityRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var database = redis.GetDatabase();
            var members = await database.SetMembersAsync($"runner-pool:{request.Pool}:members");
            foreach (var member in members.OrderBy(value => value.ToString(), StringComparer.Ordinal))
            {
                var runnerId = member.ToString();
                if (string.IsNullOrWhiteSpace(runnerId)) continue;
                var claimed = (long)await database.ScriptEvaluateAsync(
                    ClaimScript,
                    [
                        new RedisKey($"runner:{runnerId}:heartbeat"),
                        new RedisKey($"runner:{runnerId}:capacity"),
                        new RedisKey($"runner-claim:{request.RuntimeInstanceId:N}")
                    ],
                    [
                        request.MemoryBytes,
                        request.NanoCpus,
                        request.PidsLimit,
                        runnerId
                    ]);
                if (claimed == 1)
                    return new(RunnerCapacityAvailability.Claimed, runnerId);
            }
            return new(RunnerCapacityAvailability.Insufficient);
        }
        catch (RedisException)
        {
            return new(RunnerCapacityAvailability.Unavailable);
        }
    }

    public async Task ReleaseAsync(
        Guid runtimeInstanceId,
        string runnerId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = redis.GetDatabase();
        await database.ScriptEvaluateAsync(
            ReleaseScript,
            [
                new RedisKey($"runner:{runnerId}:capacity"),
                new RedisKey($"runner-claim:{runtimeInstanceId:N}")
            ],
            []);
    }
}
