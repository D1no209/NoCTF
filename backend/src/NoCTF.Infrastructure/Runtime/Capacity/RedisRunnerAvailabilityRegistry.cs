using System.Diagnostics;
using NoCTF.Application.Observability;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Capacity;
using System.Text.Json;
using NoCTF.Domain.Runtime;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Runtime.Capacity;

public enum RunnerAvailabilityRegistrationOutcome
{
    Online,
    OfflineCapacityUntrusted,
    OfflineProviderUnavailable,
    OfflineAdmissionBlocked
}

public sealed record RunnerAvailabilityRegistration(
    string RunnerPool,
    string RunnerId,
    RuntimeProvider Provider,
    string Version,
    RuntimeResourceLimits Capacity,
    TimeSpan TimeToLive,
    bool HasActiveAssignments,
    bool ProviderAvailable = true,
    RunnerAdmissionSnapshot? Admission = null,
    RunnerAdmissionOptions? AdmissionOptions = null,
    bool ActualUsage = false);

public sealed class RedisRunnerAvailabilityRegistry(IConnectionMultiplexer redis)
{
    private const string LegacyRegistrationSchema = "2";
    private const string ActualUsageRegistrationSchema = "3";
    private const double CandidateJitterMaximum = 0.000001d;

    private const string RegisterScript = RunnerAdmissionLua.Functions + "\n" + """
        redis.call('SADD', KEYS[1], ARGV[1])
        redis.call('SADD', 'runner-registry:nodes', ARGV[1])
        if ARGV[28] == '1' then
            redis.call('HSET', KEYS[2], 'observationRequired', '1', 'observationFreshUntil', ARGV[13],
                'observation', ARGV[11], 'resourceDomain', ARGV[14], 'maxPrimary', ARGV[15],
                'maxAuxiliary', ARGV[16], 'observationObservedAt', ARGV[17])
        end

        if redis.call('HGET', KEYS[2], 'admissionState') == 'reconciling' then
            redis.call('SET', KEYS[3], ARGV[7], 'PX', ARGV[6])
            redis.call('ZREM', KEYS[4], ARGV[1])
            return { 0, 0, 0, 0 }
        end

        if ARGV[9] ~= '1' then
            redis.call('SET', KEYS[3], ARGV[7], 'PX', ARGV[6])
            redis.call('ZREM', KEYS[4], ARGV[1])
            if redis.call('EXISTS', KEYS[2]) == 1 then
                redis.call('HSET', KEYS[2], 'admissionState', 'providerUnavailable')
                redis.call('PERSIST', KEYS[2])
            end
            return { 2, 0, 0, 0 }
        end

        local modern = ARGV[2] == '3'
        local trusted = redis.call('HGET', KEYS[2], 'registrationSchema') == ARGV[2]
        if modern then
            trusted = trusted and redis.call('HEXISTS', KEYS[2], 'observedTotalMemoryBytes') == 1
                and redis.call('HEXISTS', KEYS[2], 'observedTotalNanoCpus') == 1
                and redis.call('HEXISTS', KEYS[2], 'startupReservedMemoryBytes') == 1
        else
            trusted = trusted and redis.call('HEXISTS', KEYS[2], 'totalMemoryBytes') == 1
                and redis.call('HEXISTS', KEYS[2], 'totalNanoCpus') == 1
                and redis.call('HEXISTS', KEYS[2], 'totalPids') == 1
        end

        if not trusted then
            local wasOnline = redis.call('EXISTS', KEYS[3])
            redis.call('SET', KEYS[3], ARGV[7], 'PX', ARGV[6])
            redis.call('ZREM', KEYS[4], ARGV[1])
            if ARGV[8] == '1' or wasOnline == 1 then return { 0, 0, 0, 0 } end
            if modern and ARGV[29] ~= '1' then return { 0, 0, 0, 0 } end
            if modern then
                redis.call('HSET', KEYS[2], 'registrationSchema', ARGV[2],
                    'startupReservedMemoryBytes', 0, 'startupReservedNanoCpus', 0,
                    'startupReservedPids', 0, 'startingPrimary', 0, 'startingAuxiliary', 0)
            else
                redis.call('HSET', KEYS[2],
                    'registrationSchema', ARGV[2],
                    'availableMemoryBytes', ARGV[3],
                    'availableNanoCpus', ARGV[4],
                    'availablePids', ARGV[5],
                    'totalMemoryBytes', ARGV[3],
                    'totalNanoCpus', ARGV[4],
                    'totalPids', ARGV[5])
            end
        elseif not modern then
            local fields = { 'MemoryBytes', 'NanoCpus', 'Pids' }
            for i, suffix in ipairs(fields) do
                local total = tonumber(ARGV[i + 2])
                local previous = tonumber(redis.call('HGET', KEYS[2], 'total' .. suffix))
                local available = tonumber(redis.call('HGET', KEYS[2], 'available' .. suffix))
                if not available then return { 0, 0, 0, 0 } end
                redis.call('HSET', KEYS[2], 'total' .. suffix, total,
                    'available' .. suffix, available + total - previous)
            end
        end

        if modern and ARGV[29] == '1' then
            local completed = redis.call('ZRANGEBYSCORE', KEYS[5], '-inf', tonumber(ARGV[17]))
            for _, claim in ipairs(completed) do
                local owner = redis.call('HGET', claim, 'runnerId')
                local completedAt = tonumber(redis.call('HGET', claim, 'completedAt') or '0')
                if owner == ARGV[1] and redis.call('HGET', claim, 'starting') == '1'
                    and completedAt > 0 and completedAt <= tonumber(ARGV[17]) then
                    local memory = tonumber(redis.call('HGET', claim, 'memoryBytes') or '0')
                    local cpu = tonumber(redis.call('HGET', claim, 'nanoCpus') or '0')
                    local pids = tonumber(redis.call('HGET', claim, 'pidsLimit') or '0')
                    redis.call('HSET', KEYS[2],
                        'startupReservedMemoryBytes', math.max(0, tonumber(redis.call('HGET', KEYS[2], 'startupReservedMemoryBytes') or '0') - memory),
                        'startupReservedNanoCpus', math.max(0, tonumber(redis.call('HGET', KEYS[2], 'startupReservedNanoCpus') or '0') - cpu),
                        'startupReservedPids', math.max(0, tonumber(redis.call('HGET', KEYS[2], 'startupReservedPids') or '0') - pids))
                    local field = redis.call('HGET', claim, 'auxiliary') == '1' and 'startingAuxiliary' or 'startingPrimary'
                    redis.call('HSET', KEYS[2], field,
                        math.max(0, tonumber(redis.call('HGET', KEYS[2], field) or '0') - 1))
                    redis.call('HSET', claim, 'starting', 0)
                end
                redis.call('ZREM', KEYS[5], claim)
            end
            local admissionMemory = math.max(0, tonumber(ARGV[21]) - tonumber(ARGV[24])
                - tonumber(redis.call('HGET', KEYS[2], 'startupReservedMemoryBytes') or '0'))
            local admissionCpu = math.max(0, tonumber(ARGV[22]) - tonumber(ARGV[25])
                - tonumber(redis.call('HGET', KEYS[2], 'startupReservedNanoCpus') or '0'))
            local admissionPids = math.max(0, tonumber(ARGV[23]) - tonumber(ARGV[26])
                - tonumber(redis.call('HGET', KEYS[2], 'startupReservedPids') or '0'))
            redis.call('HSET', KEYS[2],
                'observedTotalMemoryBytes', ARGV[18], 'observedTotalNanoCpus', ARGV[19], 'observedTotalPids', ARGV[20],
                'observedAvailableMemoryBytes', ARGV[21], 'observedAvailableNanoCpus', ARGV[22], 'observedAvailablePids', ARGV[23],
                'safetyHeadroomMemoryBytes', ARGV[24], 'safetyHeadroomNanoCpus', ARGV[25], 'safetyHeadroomPids', ARGV[26],
                'pidsObserved', ARGV[27], 'admissionAvailableMemoryBytes', admissionMemory,
                'admissionAvailableNanoCpus', admissionCpu, 'admissionAvailablePids', admissionPids)
        end

        redis.call('PERSIST', KEYS[2])
        redis.call('HSET', KEYS[2], 'admissionState', ARGV[12])
        redis.call('SET', KEYS[3], ARGV[7], 'PX', ARGV[6])
        if ARGV[12] ~= 'ready' then
            redis.call('ZREM', KEYS[4], ARGV[1])
            return { 3, 0, 0, 0 }
        end
        redis.call('ZADD', KEYS[4], admission_pressure(KEYS[2], tonumber(ARGV[10])), ARGV[1])
        return {
            1,
            redis.call('HGET', KEYS[2], modern and 'admissionAvailableMemoryBytes' or 'availableMemoryBytes'),
            redis.call('HGET', KEYS[2], modern and 'admissionAvailableNanoCpus' or 'availableNanoCpus'),
            redis.call('HGET', KEYS[2], modern and 'admissionAvailablePids' or 'availablePids')
        }
        """;

    public async Task<RunnerAvailabilityRegistrationOutcome> RegisterAsync(
        RunnerAvailabilityRegistration registration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.RunnerPool);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.RunnerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Version);
        var projection = registration.Admission?.Capacity;
        if (!registration.ActualUsage)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(registration.Capacity.MemoryBytes, 0);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(registration.Capacity.NanoCpus, 0);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(registration.Capacity.PidsLimit, 0);
        }
        else if (registration.Admission is null)
        {
            throw new ArgumentException("Actual-usage registration requires an admission snapshot.", nameof(registration));
        }
        else if (projection is not null && (projection.ObservedTotal.MemoryBytes <= 0
            || projection.ObservedTotal.NanoCpus <= 0
            || projection.ObservedAvailable.MemoryBytes < 0
            || projection.ObservedAvailable.NanoCpus < 0
            || projection.SafetyHeadroom.MemoryBytes < 0
            || projection.SafetyHeadroom.NanoCpus < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(registration), "Observed capacity values are invalid.");
        }
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(registration.TimeToLive, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        var startedAt = Stopwatch.GetTimestamp();
        var outcome = "success";
        try
        {
            var database = redis.GetDatabase();
            var capacityKey = new RedisKey($"runner:{registration.RunnerId}:capacity");
            var heartbeat = $"provider={registration.Provider};version={registration.Version}";
            var result = (RedisResult[]?)await database.ScriptEvaluateAsync(
                RegisterScript,
                [
                    new RedisKey($"runner-pool:{registration.RunnerPool}:members"),
                    capacityKey,
                    new RedisKey($"runner:{registration.RunnerId}:heartbeat"),
                    new RedisKey($"runner-pool:{registration.RunnerPool}:candidates"),
                    new RedisKey($"runner:{registration.RunnerId}:completed-startups")
                ],
                [
                    registration.RunnerId,
                    registration.ActualUsage ? ActualUsageRegistrationSchema : LegacyRegistrationSchema,
                    registration.Capacity.MemoryBytes,
                    registration.Capacity.NanoCpus,
                    registration.Capacity.PidsLimit,
                    checked((long)registration.TimeToLive.TotalMilliseconds),
                    heartbeat,
                    registration.HasActiveAssignments ? 1 : 0,
                    registration.ProviderAvailable ? 1 : 0,
                    Random.Shared.NextDouble() * CandidateJitterMaximum,
                    JsonSerializer.Serialize(registration.Admission),
                    AdmissionState(registration.Admission?.State ?? RunnerAdmissionState.Ready),
                    registration.Admission?.Observation?.ObservedAt.AddSeconds(registration.AdmissionOptions?.FreshnessSeconds ?? 15).ToUnixTimeMilliseconds() ?? 0,
                    registration.Admission?.Observation?.ResourceDomain ?? registration.RunnerId,
                    registration.AdmissionOptions?.MainStartupConcurrency ?? 0,
                    registration.AdmissionOptions?.AuxiliaryConcurrency ?? 0,
                    registration.Admission?.Observation?.ObservedAt.ToUnixTimeMilliseconds() ?? 0,
                    projection?.ObservedTotal.MemoryBytes ?? 0,
                    projection?.ObservedTotal.NanoCpus ?? 0,
                    projection?.ObservedTotal.PidsLimit ?? 0,
                    projection?.ObservedAvailable.MemoryBytes ?? 0,
                    projection?.ObservedAvailable.NanoCpus ?? 0,
                    projection?.ObservedAvailable.PidsLimit ?? 0,
                    projection?.SafetyHeadroom.MemoryBytes ?? 0,
                    projection?.SafetyHeadroom.NanoCpus ?? 0,
                    projection?.SafetyHeadroom.PidsLimit ?? 0,
                    projection?.ObservedTotal.PidsLimit is null ? 0 : 1,
                    registration.Admission is null ? 0 : 1,
                    projection is null ? 0 : 1
                ]);

            if (result is not { Length: 4 })
                throw new InvalidOperationException("Redis returned an invalid runner registration result.");

            var registrationOutcome = (long)result[0] switch
            {
                1 => RunnerAvailabilityRegistrationOutcome.Online,
                2 => RunnerAvailabilityRegistrationOutcome.OfflineProviderUnavailable,
                3 => RunnerAvailabilityRegistrationOutcome.OfflineAdmissionBlocked,
                _ => RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted
            };
            var online = registrationOutcome == RunnerAvailabilityRegistrationOutcome.Online;
            var total = projection?.ObservedTotal;
            NoCtfTelemetry.UpdateRunnerCapacitySnapshot(
                registration.RunnerPool,
                registration.RunnerId,
                online,
                ReadCapacity(result, 1),
                total?.MemoryBytes ?? registration.Capacity.MemoryBytes,
                ReadCapacity(result, 2),
                total?.NanoCpus ?? registration.Capacity.NanoCpus,
                ReadCapacity(result, 3),
                total?.PidsLimit ?? registration.Capacity.PidsLimit);
            return registrationOutcome;
        }
        catch
        {
            outcome = "failure";
            NoCtfTelemetry.UpdateRunnerCapacitySnapshot(
                registration.RunnerPool,
                registration.RunnerId,
                false,
                0,
                projection?.ObservedTotal.MemoryBytes ?? registration.Capacity.MemoryBytes,
                0,
                projection?.ObservedTotal.NanoCpus ?? registration.Capacity.NanoCpus,
                0,
                projection?.ObservedTotal.PidsLimit ?? registration.Capacity.PidsLimit);
            throw;
        }
        finally
        {
            NoCtfTelemetry.RecordRedisOperation(
                "runner_heartbeat",
                outcome,
                Stopwatch.GetElapsedTime(startedAt).TotalSeconds);
        }
    }

    private static long ReadCapacity(IReadOnlyList<RedisResult> values, int index) =>
        values.Count > index && long.TryParse(values[index].ToString(), out var value)
            ? Math.Max(0, value)
            : 0;

    private static string AdmissionState(RunnerAdmissionState state) => state switch
    {
        RunnerAdmissionState.Ready => "ready",
        RunnerAdmissionState.Reconciling => "reconciling",
        RunnerAdmissionState.PressureBlocked => "pressureBlocked",
        RunnerAdmissionState.ProviderUnavailable => "providerUnavailable",
        RunnerAdmissionState.Draining => "draining",
        _ => "starting"
    };
}
