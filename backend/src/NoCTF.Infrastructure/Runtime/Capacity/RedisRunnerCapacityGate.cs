using StackExchange.Redis;
using NoCTF.Application.Runtime.Capacity;

namespace NoCTF.Infrastructure.Runtime.Capacity;

public sealed class RedisRunnerCapacityGate(IConnectionMultiplexer redis) : IRunnerCapacityGate
{
    private const string HeartbeatScript = """
        if redis.call('SISMEMBER', KEYS[1], ARGV[1]) == 0 then return 0 end
        if redis.call('EXISTS', KEYS[2]) == 0 then return 0 end
        return 1
        """;

    private const string ClaimScript = """
        if redis.call('SISMEMBER', KEYS[4], ARGV[4]) == 0 then return 0 end
        if redis.call('EXISTS', KEYS[1]) == 0 then return 0 end
        local existingRunner = redis.call('HGET', KEYS[3], 'runnerId')
        if existingRunner then
            if existingRunner == ARGV[4] then return 2 end
            return 0
        end
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
        local owner = redis.call('HGET', KEYS[2], 'runnerId')
        if not owner then return 0 end
        if owner ~= ARGV[1] then return -1 end
        local memory = redis.call('HGET', KEYS[2], 'memoryBytes')
        local cpu = redis.call('HGET', KEYS[2], 'nanoCpus')
        local pids = redis.call('HGET', KEYS[2], 'pidsLimit')
        if not memory or not cpu or not pids then return 0 end
        local availableMemory = tonumber(redis.call('HGET', KEYS[1], 'availableMemoryBytes') or '0')
        local availableCpu = tonumber(redis.call('HGET', KEYS[1], 'availableNanoCpus') or '0')
        local availablePids = tonumber(redis.call('HGET', KEYS[1], 'availablePids') or '0')
        local totalMemory = tonumber(redis.call('HGET', KEYS[1], 'totalMemoryBytes') or '-1')
        local totalCpu = tonumber(redis.call('HGET', KEYS[1], 'totalNanoCpus') or '-1')
        local totalPids = tonumber(redis.call('HGET', KEYS[1], 'totalPids') or '-1')
        local restoredMemory = availableMemory + tonumber(memory)
        local restoredCpu = availableCpu + tonumber(cpu)
        local restoredPids = availablePids + tonumber(pids)
        if totalMemory >= 0 and restoredMemory > totalMemory then restoredMemory = totalMemory end
        if totalCpu >= 0 and restoredCpu > totalCpu then restoredCpu = totalCpu end
        if totalPids >= 0 and restoredPids > totalPids then restoredPids = totalPids end
        redis.call('HSET', KEYS[1],
            'availableMemoryBytes', restoredMemory,
            'availableNanoCpus', restoredCpu,
            'availablePids', restoredPids)
        redis.call('DEL', KEYS[2])
        return 1
        """;

    public async Task<RunnerHeartbeatStatus> GetHeartbeatAsync(
        string runnerPool,
        string runnerId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerPool);
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerId);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var database = redis.GetDatabase();
            var live = (long)await database.ScriptEvaluateAsync(
                HeartbeatScript,
                [
                    new RedisKey($"runner-pool:{runnerPool}:members"),
                    new RedisKey($"runner:{runnerId}:heartbeat")
                ],
                [runnerId]);
            return live == 1 ? RunnerHeartbeatStatus.Online : RunnerHeartbeatStatus.Offline;
        }
        catch (RedisException)
        {
            return RunnerHeartbeatStatus.Unavailable;
        }
    }

    public async Task<RunnerPoolInventory> GetPoolInventoryAsync(
        string runnerPool,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerPool);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var members = await redis.GetDatabase()
                .SetMembersAsync($"runner-pool:{runnerPool}:members");
            return new(
                RunnerPoolInventoryAvailability.Available,
                members.Select(member => member.ToString())
                    .Where(member => !string.IsNullOrWhiteSpace(member))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
        }
        catch (RedisException)
        {
            return new(
                RunnerPoolInventoryAvailability.Unavailable,
                []);
        }
    }

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
                        new RedisKey($"runner-claim:{request.RuntimeInstanceId:N}"),
                        new RedisKey($"runner-pool:{request.Pool}:members")
                    ],
                    [
                        request.MemoryBytes,
                        request.NanoCpus,
                        request.PidsLimit,
                        runnerId
                    ]);
                if (claimed == 1 || claimed == 2)
                    return new(
                        RunnerCapacityAvailability.Claimed,
                        runnerId,
                        claimed == 1
                            ? RunnerCapacityClaimState.Acquired
                            : RunnerCapacityClaimState.AlreadyOwned);
            }
            return new(RunnerCapacityAvailability.Insufficient);
        }
        catch (RedisException)
        {
            return new(RunnerCapacityAvailability.Unavailable);
        }
    }

    public async Task<RunnerCapacityClaim> TryClaimForRunnerAsync(
        RunnerCapacityRequest request,
        string runnerId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerId);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var database = redis.GetDatabase();
            var claimed = (long)await database.ScriptEvaluateAsync(
                ClaimScript,
                [
                    new RedisKey($"runner:{runnerId}:heartbeat"),
                    new RedisKey($"runner:{runnerId}:capacity"),
                    new RedisKey($"runner-claim:{request.RuntimeInstanceId:N}"),
                    new RedisKey($"runner-pool:{request.Pool}:members")
                ],
                [
                    request.MemoryBytes,
                    request.NanoCpus,
                    request.PidsLimit,
                    runnerId
                ]);
            return claimed == 1 || claimed == 2
                ? new(
                    RunnerCapacityAvailability.Claimed,
                    runnerId,
                    claimed == 1
                        ? RunnerCapacityClaimState.Acquired
                        : RunnerCapacityClaimState.AlreadyOwned)
                : new(RunnerCapacityAvailability.Insufficient);
        }
        catch (RedisException)
        {
            return new(RunnerCapacityAvailability.Unavailable);
        }
    }

    public async Task<RunnerCapacityReleaseOutcome> ReleaseAsync(
        Guid runtimeInstanceId,
        string runnerId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = redis.GetDatabase();
        var released = (long)await database.ScriptEvaluateAsync(
            ReleaseScript,
            [
                new RedisKey($"runner:{runnerId}:capacity"),
                new RedisKey($"runner-claim:{runtimeInstanceId:N}")
            ],
            [runnerId]);
        return released switch
        {
            1 => RunnerCapacityReleaseOutcome.Released,
            0 => RunnerCapacityReleaseOutcome.AlreadyReleased,
            -1 => RunnerCapacityReleaseOutcome.OwnerMismatch,
            _ => throw new InvalidOperationException("Redis returned an unknown capacity release result.")
        };
    }
}
