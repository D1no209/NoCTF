using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Runtime.Capacity;

public enum RunnerAvailabilityRegistrationOutcome
{
    Online,
    OfflineCapacityUntrusted,
    OfflineProviderUnavailable
}

public sealed record RunnerAvailabilityRegistration(
    string RunnerPool,
    string RunnerId,
    RuntimeProvider Provider,
    string Version,
    RuntimeResourceLimits Capacity,
    TimeSpan TimeToLive,
    bool HasActiveAssignments,
    bool ProviderAvailable = true);

public sealed class RedisRunnerAvailabilityRegistry(IConnectionMultiplexer redis)
{
    private const string RegistrationSchema = "1";
    private const double CandidateJitterMaximum = 0.000001d;

    private const string RegisterScript = """
        local function pressure(capacityKey, jitter)
            local availableMemory = tonumber(redis.call('HGET', capacityKey, 'availableMemoryBytes') or '0')
            local availableCpu = tonumber(redis.call('HGET', capacityKey, 'availableNanoCpus') or '0')
            local availablePids = tonumber(redis.call('HGET', capacityKey, 'availablePids') or '0')
            local totalMemory = tonumber(redis.call('HGET', capacityKey, 'totalMemoryBytes') or '0')
            local totalCpu = tonumber(redis.call('HGET', capacityKey, 'totalNanoCpus') or '0')
            local totalPids = tonumber(redis.call('HGET', capacityKey, 'totalPids') or '0')
            if totalMemory <= 0 or totalCpu <= 0 or totalPids <= 0 then return 1 + jitter end
            return math.max(
                1 - (availableMemory / totalMemory),
                1 - (availableCpu / totalCpu),
                1 - (availablePids / totalPids)) + jitter
        end

        redis.call('SADD', KEYS[1], ARGV[1])

        if ARGV[9] ~= '1' then
            redis.call('DEL', KEYS[3])
            redis.call('ZREM', KEYS[4], ARGV[1])
            if redis.call('EXISTS', KEYS[2]) == 1 then
                redis.call('PEXPIRE', KEYS[2], ARGV[6])
            end
            return 2
        end

        local trusted = redis.call('HGET', KEYS[2], 'registrationSchema') == ARGV[2]
            and redis.call('HGET', KEYS[2], 'totalMemoryBytes') == ARGV[3]
            and redis.call('HGET', KEYS[2], 'totalNanoCpus') == ARGV[4]
            and redis.call('HGET', KEYS[2], 'totalPids') == ARGV[5]

        if not trusted then
            local wasOnline = redis.call('EXISTS', KEYS[3])
            redis.call('DEL', KEYS[3])
            redis.call('ZREM', KEYS[4], ARGV[1])
            if ARGV[8] == '1' or wasOnline == 1 then return 0 end
            redis.call('HSET', KEYS[2],
                'registrationSchema', ARGV[2],
                'availableMemoryBytes', ARGV[3],
                'availableNanoCpus', ARGV[4],
                'availablePids', ARGV[5],
                'totalMemoryBytes', ARGV[3],
                'totalNanoCpus', ARGV[4],
                'totalPids', ARGV[5])
        end

        redis.call('PEXPIRE', KEYS[2], ARGV[6])
        redis.call('SET', KEYS[3], ARGV[7], 'PX', ARGV[6])
        redis.call('ZADD', KEYS[4], pressure(KEYS[2], tonumber(ARGV[10])), ARGV[1])
        return 1
        """;

    public async Task<RunnerAvailabilityRegistrationOutcome> RegisterAsync(
        RunnerAvailabilityRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.RunnerPool);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.RunnerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Version);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(registration.Capacity.MemoryBytes, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(registration.Capacity.NanoCpus, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(registration.Capacity.PidsLimit, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(registration.TimeToLive, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        var heartbeat = $"provider={registration.Provider};version={registration.Version}";
        var result = (long)await redis.GetDatabase().ScriptEvaluateAsync(
            RegisterScript,
            [
                new RedisKey($"runner-pool:{registration.RunnerPool}:members"),
                new RedisKey($"runner:{registration.RunnerId}:capacity"),
                new RedisKey($"runner:{registration.RunnerId}:heartbeat"),
                new RedisKey($"runner-pool:{registration.RunnerPool}:candidates")
            ],
            [
                registration.RunnerId,
                RegistrationSchema,
                registration.Capacity.MemoryBytes,
                registration.Capacity.NanoCpus,
                registration.Capacity.PidsLimit,
                checked((long)registration.TimeToLive.TotalMilliseconds),
                heartbeat,
                registration.HasActiveAssignments ? 1 : 0,
                registration.ProviderAvailable ? 1 : 0,
                Random.Shared.NextDouble() * CandidateJitterMaximum
            ]);

        return result switch
        {
            1 => RunnerAvailabilityRegistrationOutcome.Online,
            2 => RunnerAvailabilityRegistrationOutcome.OfflineProviderUnavailable,
            _ => RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted
        };
    }
}
