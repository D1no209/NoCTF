using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Caching;

public enum RunnerAvailabilityRegistrationOutcome
{
    Online,
    OfflineCapacityUntrusted
}

public sealed record RunnerAvailabilityRegistration(
    string RunnerPool,
    string RunnerId,
    RuntimeProvider Provider,
    string Version,
    RuntimeResourceLimits Capacity,
    TimeSpan TimeToLive,
    bool HasActiveAssignments);

public sealed class RedisRunnerAvailabilityRegistry(IConnectionMultiplexer redis)
{
    private const string RegistrationSchema = "1";

    private const string RegisterScript = """
        redis.call('SADD', KEYS[1], ARGV[1])

        local trusted = redis.call('HGET', KEYS[2], 'registrationSchema') == ARGV[2]
            and redis.call('HGET', KEYS[2], 'totalMemoryBytes') == ARGV[3]
            and redis.call('HGET', KEYS[2], 'totalNanoCpus') == ARGV[4]
            and redis.call('HGET', KEYS[2], 'totalPids') == ARGV[5]

        if not trusted then
            local wasOnline = redis.call('EXISTS', KEYS[3])
            redis.call('DEL', KEYS[3])
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
                new RedisKey($"runner:{registration.RunnerId}:heartbeat")
            ],
            [
                registration.RunnerId,
                RegistrationSchema,
                registration.Capacity.MemoryBytes,
                registration.Capacity.NanoCpus,
                registration.Capacity.PidsLimit,
                checked((long)registration.TimeToLive.TotalMilliseconds),
                heartbeat,
                registration.HasActiveAssignments ? 1 : 0
            ]);

        return result == 1
            ? RunnerAvailabilityRegistrationOutcome.Online
            : RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted;
    }
}
