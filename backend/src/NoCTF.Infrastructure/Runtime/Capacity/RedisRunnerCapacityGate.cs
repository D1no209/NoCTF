using System.Diagnostics;
using NoCTF.Application.Observability;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Runtime;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Runtime.Capacity;

public sealed class RedisRunnerCapacityGate(IConnectionMultiplexer redis, TimeProvider? clock = null) : IRunnerCapacityGate
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    public async Task RecordWaitingAsync(Guid runtimeInstanceId, RunnerAdmissionFailure? failure, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = $"runtime-wait:{runtimeInstanceId:N}";
        if (failure is null) await redis.GetDatabase().KeyDeleteAsync(key);
        else await redis.GetDatabase().StringSetAsync(key, (int)failure.Value, TimeSpan.FromMinutes(1));
    }

    public async Task<IReadOnlyDictionary<Guid, RunnerAdmissionFailure>> ReadWaitingAsync(IReadOnlyList<Guid> runtimeIds, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (runtimeIds.Count == 0) return new Dictionary<Guid, RunnerAdmissionFailure>();
        try
        {
            var values = await redis.GetDatabase().StringGetAsync(runtimeIds.Select(id => (RedisKey)$"runtime-wait:{id:N}").ToArray());
            var result = new Dictionary<Guid, RunnerAdmissionFailure>();
            for (var index = 0; index < runtimeIds.Count; index++)
                if (int.TryParse(values[index].ToString(), out var value) && Enum.IsDefined((RunnerAdmissionFailure)value))
                    result[runtimeIds[index]] = (RunnerAdmissionFailure)value;
            return result;
        }
        catch (RedisException)
        {
            return runtimeIds.Distinct().ToDictionary(id => id, _ => RunnerAdmissionFailure.LedgerRecovering);
        }
    }
    private const int CandidateLimit = 8;
    private const double CandidateJitterMaximum = 0.000001d;

    private const string HeartbeatScript = """
        if redis.call('SISMEMBER', KEYS[1], ARGV[1]) == 0 then return 0 end
        if redis.call('EXISTS', KEYS[2]) == 0 then return 0 end
        return 1
        """;

    private const string ClaimScript = RunnerAdmissionLua.Functions + "\n" + """
        if not ready(KEYS[1], KEYS[2]) then return 0 end
        local existingRunner = redis.call('HGET', KEYS[3], 'runnerId')
        if existingRunner then
            if existingRunner == ARGV[4] then return 2 end
            return 0
        end
        if redis.call('SISMEMBER', KEYS[4], ARGV[4]) == 0 then
            redis.call('ZREM', KEYS[5], ARGV[4])
            return 0
        end
        if redis.call('EXISTS', KEYS[1]) == 0 or redis.call('EXISTS', KEYS[2]) == 0 then
            redis.call('ZREM', KEYS[5], ARGV[4])
            return 0
        end
        local modern = schema3(KEYS[2])
        local memory = tonumber(modern and ARGV[8] or ARGV[1])
        local cpu = tonumber(modern and ARGV[9] or ARGV[2])
        local pids = tonumber(modern and ARGV[10] or ARGV[3])
        if not fits(KEYS[2], memory, cpu, pids, ARGV[7] == '1') then
            redis.call('ZADD', KEYS[5], admission_pressure(KEYS[2], tonumber(ARGV[6])), ARGV[4])
            return 0
        end
        redis.call('HINCRBY', KEYS[2], modern and 'admissionAvailableMemoryBytes' or 'availableMemoryBytes', -memory)
        redis.call('HINCRBY', KEYS[2], modern and 'admissionAvailableNanoCpus' or 'availableNanoCpus', -cpu)
        if not modern or redis.call('HGET', KEYS[2], 'pidsObserved') ~= '0' then
            redis.call('HINCRBY', KEYS[2], modern and 'admissionAvailablePids' or 'availablePids', -pids)
        end
        redis.call('HSET', KEYS[3],
            'runnerId', ARGV[4],
            'pool', ARGV[5],
            'memoryBytes', memory,
            'nanoCpus', cpu,
            'pidsLimit', pids)
        start_slot(KEYS[2], KEYS[3], ARGV[7] == '1', memory, cpu, pids)
        track_claim(KEYS[3], ARGV[4])
        redis.call('ZADD', KEYS[5], admission_pressure(KEYS[2], tonumber(ARGV[6])), ARGV[4])
        return 1
        """;

    private const string RebuildAndClaimScript = RunnerAdmissionLua.Functions + "\n" + """
        local existingRunner = redis.call('HGET', KEYS[3], 'runnerId')
        if existingRunner then
            if not ready('runner:' .. existingRunner .. ':heartbeat', 'runner:' .. existingRunner .. ':capacity') then return { 0, '' } end
            return { 2, existingRunner }
        end

        redis.call('DEL', KEYS[2])
        local bestRunner = nil
        local bestScore = nil
        local members = redis.call('SMEMBERS', KEYS[1])
        for _, runnerId in ipairs(members) do
            local heartbeatKey = 'runner:' .. runnerId .. ':heartbeat'
            local capacityKey = 'runner:' .. runnerId .. ':capacity'
            if ready(heartbeatKey, capacityKey) then
                local hash = redis.sha1hex(runnerId .. ARGV[5])
                local jitter = (tonumber(string.sub(hash, 1, 8), 16) / 4294967295) * 0.000001
                local score = admission_pressure(capacityKey, jitter)
                redis.call('ZADD', KEYS[2], score, runnerId)
                local modern = schema3(capacityKey)
                local requestedMemory = tonumber(modern and ARGV[7] or ARGV[1])
                local requestedCpu = tonumber(modern and ARGV[8] or ARGV[2])
                local requestedPids = tonumber(modern and ARGV[9] or ARGV[3])
                if fits(capacityKey, requestedMemory, requestedCpu, requestedPids, ARGV[6] == '1')
                    and (not bestScore or score < bestScore) then
                    bestRunner = runnerId
                    bestScore = score
                end
            end
        end

        if not bestRunner then return { 0, '' } end
        existingRunner = redis.call('HGET', KEYS[3], 'runnerId')
        if existingRunner then return { 2, existingRunner } end

        local bestCapacityKey = 'runner:' .. bestRunner .. ':capacity'
        local modern = schema3(bestCapacityKey)
        local requestedMemory = tonumber(modern and ARGV[7] or ARGV[1])
        local requestedCpu = tonumber(modern and ARGV[8] or ARGV[2])
        local requestedPids = tonumber(modern and ARGV[9] or ARGV[3])
        redis.call('HINCRBY', bestCapacityKey, modern and 'admissionAvailableMemoryBytes' or 'availableMemoryBytes', -requestedMemory)
        redis.call('HINCRBY', bestCapacityKey, modern and 'admissionAvailableNanoCpus' or 'availableNanoCpus', -requestedCpu)
        if not modern or redis.call('HGET', bestCapacityKey, 'pidsObserved') ~= '0' then
            redis.call('HINCRBY', bestCapacityKey, modern and 'admissionAvailablePids' or 'availablePids', -requestedPids)
        end
        redis.call('HSET', KEYS[3],
            'runnerId', bestRunner,
            'pool', ARGV[4],
            'memoryBytes', ARGV[1],
            'nanoCpus', ARGV[2],
            'pidsLimit', ARGV[3])
        start_slot(bestCapacityKey, KEYS[3], ARGV[6] == '1', requestedMemory, requestedCpu, requestedPids)
        track_claim(KEYS[3], bestRunner)
        redis.call('ZADD', KEYS[2], admission_pressure(bestCapacityKey, 0), bestRunner)
        return { 1, bestRunner }
        """;

    private const string ReleaseScript = RunnerAdmissionLua.Functions + "\n" + """
        local owner = redis.call('HGET', KEYS[2], 'runnerId')
        if not owner then return 0 end
        if owner ~= ARGV[1] then return -1 end
        if redis.call('EXISTS', KEYS[1]) == 0 then return -2 end
        if redis.call('HGET', KEYS[1], 'admissionState') == 'reconciling' then return -2 end
        local memory = redis.call('HGET', KEYS[2], 'memoryBytes')
        local cpu = redis.call('HGET', KEYS[2], 'nanoCpus')
        local pids = redis.call('HGET', KEYS[2], 'pidsLimit')
        if not memory or not cpu or not pids then return 0 end
        local modern = schema3(KEYS[1])
        if modern and redis.call('HGET', KEYS[2], 'starting') == '1' then
            local reserveFields = { 'MemoryBytes', 'NanoCpus', 'Pids' }
            local amounts = { tonumber(memory), tonumber(cpu), tonumber(pids) }
            for i, suffix in ipairs(reserveFields) do
                local current = tonumber(redis.call('HGET', KEYS[1], 'startupReserved' .. suffix) or '0')
                redis.call('HSET', KEYS[1], 'startupReserved' .. suffix, math.max(0, current - amounts[i]))
            end
            local field = redis.call('HGET', KEYS[2], 'auxiliary') == '1' and 'startingAuxiliary' or 'startingPrimary'
            local value = tonumber(redis.call('HGET', KEYS[1], field) or '0')
            redis.call('HSET', KEYS[1], field, math.max(0, value - 1))
            local baseMemory = math.max(0, tonumber(redis.call('HGET', KEYS[1], 'observedAvailableMemoryBytes') or '0')
                - tonumber(redis.call('HGET', KEYS[1], 'safetyHeadroomMemoryBytes') or '0'))
            local baseCpu = math.max(0, tonumber(redis.call('HGET', KEYS[1], 'observedAvailableNanoCpus') or '0')
                - tonumber(redis.call('HGET', KEYS[1], 'safetyHeadroomNanoCpus') or '0'))
            local basePids = math.max(0, tonumber(redis.call('HGET', KEYS[1], 'observedAvailablePids') or '0')
                - tonumber(redis.call('HGET', KEYS[1], 'safetyHeadroomPids') or '0'))
            redis.call('HSET', KEYS[1],
                'admissionAvailableMemoryBytes', math.max(0, baseMemory - tonumber(redis.call('HGET', KEYS[1], 'startupReservedMemoryBytes') or '0')),
                'admissionAvailableNanoCpus', math.max(0, baseCpu - tonumber(redis.call('HGET', KEYS[1], 'startupReservedNanoCpus') or '0')),
                'admissionAvailablePids', math.max(0, basePids - tonumber(redis.call('HGET', KEYS[1], 'startupReservedPids') or '0')))
        elseif not modern then
            local availableMemory = tonumber(redis.call('HGET', KEYS[1], 'availableMemoryBytes') or '0')
            local availableCpu = tonumber(redis.call('HGET', KEYS[1], 'availableNanoCpus') or '0')
            local availablePids = tonumber(redis.call('HGET', KEYS[1], 'availablePids') or '0')
            local totalMemory = tonumber(redis.call('HGET', KEYS[1], 'totalMemoryBytes') or '-1')
            local totalCpu = tonumber(redis.call('HGET', KEYS[1], 'totalNanoCpus') or '-1')
            local totalPids = tonumber(redis.call('HGET', KEYS[1], 'totalPids') or '-1')
            redis.call('HSET', KEYS[1],
                'availableMemoryBytes', math.min(totalMemory, availableMemory + tonumber(memory)),
                'availableNanoCpus', math.min(totalCpu, availableCpu + tonumber(cpu)),
                'availablePids', math.min(totalPids, availablePids + tonumber(pids)))
            if redis.call('HGET', KEYS[2], 'starting') == '1' then
                local field = redis.call('HGET', KEYS[2], 'auxiliary') == '1' and 'activeAuxiliary' or 'startingPrimary'
                local value = tonumber(redis.call('HGET', KEYS[1], field) or '0')
                redis.call('HSET', KEYS[1], field, math.max(0, value - 1))
            end
        end
        redis.call('DEL', KEYS[2])
        redis.call('ZREM', KEYS[6], KEYS[2])
        redis.call('ZREM', 'runner:' .. ARGV[1] .. ':unconfirmed-claims', KEYS[2])
        if redis.call('SISMEMBER', KEYS[4], ARGV[1]) == 1
            and redis.call('EXISTS', KEYS[3]) == 1
            and redis.call('EXISTS', KEYS[1]) == 1 then
            redis.call('ZADD', KEYS[5], admission_pressure(KEYS[1], tonumber(ARGV[2])), ARGV[1])
        else
            redis.call('ZREM', KEYS[5], ARGV[1])
        end
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
        var startedAt = Stopwatch.GetTimestamp();
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
            var result = live == 1 ? RunnerHeartbeatStatus.Online : RunnerHeartbeatStatus.Offline;
            RecordRedisOperation("runner_heartbeat_check", "success", startedAt);
            return result;
        }
        catch (RedisException)
        {
            RecordRedisOperation("runner_heartbeat_check", "failure", startedAt);
            return RunnerHeartbeatStatus.Unavailable;
        }
    }

    public async Task<RunnerPoolInventory> GetPoolInventoryAsync(
        string runnerPool,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerPool);
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var members = await redis.GetDatabase()
                .SetMembersAsync($"runner-pool:{runnerPool}:members");
            var result = new RunnerPoolInventory(
                RunnerPoolInventoryAvailability.Available,
                members.Select(member => member.ToString())
                    .Where(member => !string.IsNullOrWhiteSpace(member))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
            RecordRedisOperation("runner_inventory", "success", startedAt);
            return result;
        }
        catch (RedisException)
        {
            RecordRedisOperation("runner_inventory", "failure", startedAt);
            return new(
                RunnerPoolInventoryAvailability.Unavailable,
                []);
        }
    }

    public async Task<RunnerCapacityClaim> TryClaimAsync(
        RunnerCapacityRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = Stopwatch.GetTimestamp();
        var attempts = 0;
        try
        {
            var database = redis.GetDatabase();
            var claimKey = new RedisKey($"runner-claim:{request.ClaimSuffix}");
            var existingRunner = (string?)await database.HashGetAsync(claimKey, "runnerId");
            if (!string.IsNullOrWhiteSpace(existingRunner))
            {
                attempts++;
                var existing = await TryClaimCandidateAsync(
                    database,
                    request,
                    existingRunner,
                    cancellationToken);
                RecordClaim(request.Pool, existing.Availability, attempts, startedAt);
                return existing;
            }

            var candidateKey = new RedisKey($"runner-pool:{request.Pool}:candidates");
            var candidates = await database.SortedSetRangeByRankAsync(
                candidateKey,
                0,
                CandidateLimit - 1,
                Order.Ascending);
            RunnerAdmissionFailure? failure = null;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var runnerId = candidate.ToString();
                if (string.IsNullOrWhiteSpace(runnerId)) continue;
                attempts++;
                var claim = await TryClaimCandidateAsync(
                    database,
                    request,
                    runnerId,
                    cancellationToken);
                if (claim.Availability != RunnerCapacityAvailability.Claimed)
                {
                    failure ??= claim.Failure;
                    continue;
                }
                RecordClaim(request.Pool, claim.Availability, attempts, startedAt);
                return claim;
            }

            attempts++;
            var rebuilt = await RebuildAndClaimAsync(database, request, cancellationToken);
            if (rebuilt.Availability != RunnerCapacityAvailability.Claimed)
            {
                var members = await database.SetMembersAsync($"runner-pool:{request.Pool}:members");
                var allExceed = members.Length is > 0 and <= 128;
                RunnerAdmissionFailure? poolFailure = null;
                foreach (var member in members.Take(128))
                {
                    var observed = await ReadFailureAsync(database, request, member.ToString(), cancellationToken);
                    allExceed &= observed == RunnerAdmissionFailure.RequestExceedsNodeCapacity;
                    if (observed is not (RunnerAdmissionFailure.NoEligibleRunner or RunnerAdmissionFailure.RequestExceedsNodeCapacity))
                        poolFailure ??= observed;
                }
                // An oversized candidate is not proof that every configured node is too small.
                // Incomplete/offline inventory keeps this conclusion unknown.
                rebuilt = rebuilt with { Failure = allExceed ? RunnerAdmissionFailure.RequestExceedsNodeCapacity
                    : poolFailure ?? (failure is not RunnerAdmissionFailure.RequestExceedsNodeCapacity
                        ? failure : null) ?? RunnerAdmissionFailure.NoEligibleRunner };
            }
            RecordClaim(request.Pool, rebuilt.Availability, attempts, startedAt);
            return rebuilt;
        }
        catch (RedisException)
        {
            RecordClaim(
                request.Pool,
                RunnerCapacityAvailability.Unavailable,
                Math.Max(attempts, 1),
                startedAt);
            return new(RunnerCapacityAvailability.Unavailable);
        }
    }

    public async Task<RunnerCapacityClaim> TryClaimForRunnerAsync(
        RunnerCapacityRequest request,
        string runnerId,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerId);
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            var result = await TryClaimCandidateAsync(
                redis.GetDatabase(),
                request,
                runnerId,
                cancellationToken);
            RecordClaim(request.Pool, result.Availability, 1, startedAt);
            return result;
        }
        catch (RedisException)
        {
            RecordClaim(request.Pool, RunnerCapacityAvailability.Unavailable, 1, startedAt);
            return new(RunnerCapacityAvailability.Unavailable);
        }
    }

    public Task<RunnerCapacityReleaseOutcome> ReleaseAsync(
        Guid runtimeInstanceId,
        string runnerId,
        CancellationToken cancellationToken) => ReleaseCoreAsync(runtimeInstanceId.ToString("N"), runnerId, cancellationToken);

    public Task<RunnerCapacityReleaseOutcome> ReleaseWorkloadAsync(
        RuntimeWorkloadIdentity identity, string runnerId, CancellationToken cancellationToken) =>
        ReleaseCoreAsync(identity.Key, runnerId, cancellationToken);

    public async Task CompleteWorkloadStartupAsync(RuntimeWorkloadIdentity identity, string runnerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await redis.GetDatabase().ScriptEvaluateAsync("""
            if redis.call('HGET', KEYS[2], 'runnerId') ~= ARGV[1] then return 0 end
            if redis.call('EXISTS', KEYS[1]) == 0 then return 0 end
            if redis.call('HGET', KEYS[2], 'auxiliary') == '1' then return 0 end
            if redis.call('HGET', KEYS[2], 'starting') == '1' then
                if redis.call('HGET', KEYS[1], 'registrationSchema') == '3' then
                    local completedAt = tonumber(redis.call('HGET', KEYS[2], 'completedAt') or '0')
                    if completedAt == 0 then
                        completedAt = tonumber(ARGV[2])
                        redis.call('HSET', KEYS[2], 'completedAt', completedAt)
                    end
                    redis.call('ZADD', KEYS[3], completedAt, KEYS[2])
                else
                    local active = tonumber(redis.call('HGET', KEYS[1], 'startingPrimary') or '0')
                    redis.call('HSET', KEYS[1], 'startingPrimary', math.max(0, active - 1))
                    redis.call('HSET', KEYS[2], 'starting', 0)
                end
            end
            return 1
            """, [$"runner:{runnerId}:capacity", $"runner-claim:{identity.Key}",
                $"runner:{runnerId}:completed-startups"], [runnerId, timeProvider.GetUtcNow().ToUnixTimeMilliseconds()]);
    }

    public async Task<bool> ValidateClaimAsync(RuntimeCapacityAllocation allocation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return (long)await redis.GetDatabase().ScriptEvaluateAsync(RunnerAdmissionLua.Functions + "\n" + """
            if not ready(KEYS[1], KEYS[2]) then return 0 end
            if redis.call('HGET', KEYS[3], 'runnerId') ~= ARGV[1] then return 0 end
            local modern = schema3(KEYS[2])
            if tonumber(redis.call('HGET', KEYS[3], 'memoryBytes') or '-1') ~= tonumber(modern and ARGV[5] or ARGV[2]) then return 0 end
            if tonumber(redis.call('HGET', KEYS[3], 'nanoCpus') or '-1') ~= tonumber(modern and ARGV[6] or ARGV[3]) then return 0 end
            if tonumber(redis.call('HGET', KEYS[3], 'pidsLimit') or '-1') ~= tonumber(modern and ARGV[7] or ARGV[4]) then return 0 end
            return 1
            """, [$"runner:{allocation.RunnerId}:heartbeat", $"runner:{allocation.RunnerId}:capacity", $"runner-claim:{allocation.Identity.Key}"],
            [allocation.RunnerId, allocation.Budget.MemoryBytes, allocation.Budget.NanoCpus, allocation.Budget.PidsLimit,
                allocation.Limit.MemoryBytes, allocation.Limit.NanoCpus, allocation.Limit.PidsLimit]) == 1;
    }

    public async Task<string> GetResourceDomainAsync(string runnerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var domain = (string?)await redis.GetDatabase().HashGetAsync($"runner:{runnerId}:capacity", "resourceDomain");
        return string.IsNullOrWhiteSpace(domain) ? runnerId : domain;
    }

    private async Task<RunnerCapacityReleaseOutcome> ReleaseCoreAsync(
        string claimSuffix, string runnerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = Stopwatch.GetTimestamp();
        var database = redis.GetDatabase();
        try
        {
            var claimKey = new RedisKey($"runner-claim:{claimSuffix}");
            var pool = (string?)await database.HashGetAsync(claimKey, "pool");
            var poolKey = string.IsNullOrWhiteSpace(pool) ? "__legacy__" : pool;
            var released = (long)await database.ScriptEvaluateAsync(
                ReleaseScript,
                [
                    new RedisKey($"runner:{runnerId}:capacity"),
                    claimKey,
                    new RedisKey($"runner:{runnerId}:heartbeat"),
                    new RedisKey($"runner-pool:{poolKey}:members"),
                    new RedisKey($"runner-pool:{poolKey}:candidates"),
                    new RedisKey($"runner:{runnerId}:completed-startups")
                ],
                [runnerId, NextJitter()]);
            var result = released switch
            {
                1 => RunnerCapacityReleaseOutcome.Released,
                0 => RunnerCapacityReleaseOutcome.AlreadyReleased,
                -1 => RunnerCapacityReleaseOutcome.OwnerMismatch,
                -2 => RunnerCapacityReleaseOutcome.RecoveryRequired,
                _ => throw new InvalidOperationException("Redis returned an unknown capacity release result.")
            };
            RecordRedisOperation("runner_release", "success", startedAt);
            return result;
        }
        catch (RedisException)
        {
            RecordRedisOperation("runner_release", "failure", startedAt);
            throw;
        }
    }

    private static async Task<RunnerCapacityClaim> TryClaimCandidateAsync(
        IDatabase database,
        RunnerCapacityRequest request,
        string runnerId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var limit = request.Limit ?? new RuntimeResourceAmount(request.MemoryBytes, request.NanoCpus, request.PidsLimit);
        var claimed = (long)await database.ScriptEvaluateAsync(
            ClaimScript,
            [
                new RedisKey($"runner:{runnerId}:heartbeat"),
                new RedisKey($"runner:{runnerId}:capacity"),
                new RedisKey($"runner-claim:{request.ClaimSuffix}"),
                new RedisKey($"runner-pool:{request.Pool}:members"),
                new RedisKey($"runner-pool:{request.Pool}:candidates")
            ],
            [
                request.MemoryBytes,
                request.NanoCpus,
                request.PidsLimit,
                runnerId,
                request.Pool,
                NextJitter(),
                request.Workload?.IsAuxiliary == true ? 1 : 0,
                limit.MemoryBytes,
                limit.NanoCpus,
                limit.PidsLimit
            ]);
        return claimed == 1 || claimed == 2
            ? new(
                RunnerCapacityAvailability.Claimed,
                runnerId,
                claimed == 1
                    ? RunnerCapacityClaimState.Acquired
                    : RunnerCapacityClaimState.AlreadyOwned)
            : new(RunnerCapacityAvailability.Insufficient, Failure: await ReadFailureAsync(database, request, runnerId, cancellationToken));
    }

    private static async Task<RunnerAdmissionFailure> ReadFailureAsync(IDatabase database,
        RunnerCapacityRequest request, string runnerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fields = (await database.HashGetAllAsync($"runner:{runnerId}:capacity"))
            .ToDictionary(field => field.Name.ToString(), field => field.Value.ToString(), StringComparer.Ordinal);
        long Read(string name) => long.TryParse(fields.GetValueOrDefault(name), out var value) ? value : 0;
        if (fields.Count == 0) return RunnerAdmissionFailure.LedgerRecovering;
        var state = fields.GetValueOrDefault("admissionState");
        if (state == "reconciling") return RunnerAdmissionFailure.LedgerRecovering;
        if (state == "providerUnavailable") return RunnerAdmissionFailure.ProviderUnavailable;
        if (state == "pressureBlocked") return RunnerAdmissionFailure.NodePressureHigh;
        if (state is "starting" or "draining") return RunnerAdmissionFailure.ObservationStale;
        if (fields.GetValueOrDefault("observationRequired") == "1"
            && Read("observationFreshUntil") < TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds())
            return RunnerAdmissionFailure.ObservationStale;
        if (!await database.KeyExistsAsync($"runner:{runnerId}:heartbeat")) return RunnerAdmissionFailure.NoEligibleRunner;
        var auxiliary = request.Workload?.IsAuxiliary == true;
        var modern = fields.GetValueOrDefault("registrationSchema") == "3";
        var requested = modern && request.Limit is { } limit
            ? limit : new RuntimeResourceAmount(request.MemoryBytes, request.NanoCpus, request.PidsLimit);
        var memoryReserve = !modern && !auxiliary ? Read("reservedMemory") : 0;
        var cpuReserve = !modern && !auxiliary ? Read("reservedCpu") : 0;
        var pidReserve = !modern && !auxiliary ? Read("reservedPids") : 0;
        var totalMemoryField = modern ? "observedTotalMemoryBytes" : "totalMemoryBytes";
        var totalCpuField = modern ? "observedTotalNanoCpus" : "totalNanoCpus";
        var totalPidsField = modern ? "observedTotalPids" : "totalPids";
        if (requested.MemoryBytes > Read(totalMemoryField) - memoryReserve
            || requested.NanoCpus > Read(totalCpuField) - cpuReserve
            || (fields.GetValueOrDefault("pidsObserved") != "0"
                && requested.PidsLimit > Read(totalPidsField) - pidReserve))
            return RunnerAdmissionFailure.RequestExceedsNodeCapacity;
        var maximum = Read(auxiliary ? "maxAuxiliary" : "maxPrimary");
        var activeField = auxiliary ? modern ? "startingAuxiliary" : "activeAuxiliary" : "startingPrimary";
        if (maximum > 0 && Read(activeField) >= maximum)
            return RunnerAdmissionFailure.StartupConcurrencyLimited;
        var availableMemoryField = modern ? "admissionAvailableMemoryBytes" : "availableMemoryBytes";
        var availableCpuField = modern ? "admissionAvailableNanoCpus" : "availableNanoCpus";
        var availablePidsField = modern ? "admissionAvailablePids" : "availablePids";
        if (requested.MemoryBytes > Read(availableMemoryField) - memoryReserve)
            return RunnerAdmissionFailure.MemoryActualCapacityInsufficient;
        if (requested.NanoCpus > Read(availableCpuField) - cpuReserve)
            return RunnerAdmissionFailure.CpuActualCapacityInsufficient;
        if (fields.GetValueOrDefault("pidsObserved") != "0" && requested.PidsLimit > Read(availablePidsField) - pidReserve)
            return RunnerAdmissionFailure.PidActualCapacityInsufficient;
        return RunnerAdmissionFailure.NoEligibleRunner;
    }

    private static async Task<RunnerCapacityClaim> RebuildAndClaimAsync(
        IDatabase database,
        RunnerCapacityRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var limit = request.Limit ?? new RuntimeResourceAmount(request.MemoryBytes, request.NanoCpus, request.PidsLimit);
        var result = (RedisResult[]?)await database.ScriptEvaluateAsync(
            RebuildAndClaimScript,
            [
                new RedisKey($"runner-pool:{request.Pool}:members"),
                new RedisKey($"runner-pool:{request.Pool}:candidates"),
                new RedisKey($"runner-claim:{request.ClaimSuffix}")
            ],
            [
                request.MemoryBytes,
                request.NanoCpus,
                request.PidsLimit,
                request.Pool,
                Random.Shared.NextInt64(),
                request.Workload?.IsAuxiliary == true ? 1 : 0,
                limit.MemoryBytes,
                limit.NanoCpus,
                limit.PidsLimit
            ]);
        if (result is not { Length: 2 })
            throw new InvalidOperationException("Redis returned an invalid runner index rebuild result.");

        var state = (long)result[0];
        var runnerId = (string?)result[1];
        return state switch
        {
            1 when !string.IsNullOrWhiteSpace(runnerId) => new(
                RunnerCapacityAvailability.Claimed,
                runnerId,
                RunnerCapacityClaimState.Acquired),
            2 when !string.IsNullOrWhiteSpace(runnerId) => new(
                RunnerCapacityAvailability.Claimed,
                runnerId,
                RunnerCapacityClaimState.AlreadyOwned),
            0 => new(RunnerCapacityAvailability.Insufficient),
            _ => throw new InvalidOperationException("Redis returned an unknown runner index rebuild state.")
        };
    }

    private static double NextJitter() => Random.Shared.NextDouble() * CandidateJitterMaximum;

    private static void ValidateRequest(RunnerCapacityRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Pool);
        if (request.RuntimeInstanceId == Guid.Empty || request.MemoryBytes <= 0 || request.NanoCpus <= 0 || request.PidsLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Capacity requests require positive resource amounts and a Runtime identity.");
        if (request.Workload is { } identity)
        {
            identity.Validate();
            if (identity.RuntimeInstanceId != request.RuntimeInstanceId)
                throw new ArgumentException("The workload identity belongs to another Runtime.", nameof(request));
        }
    }

    private static void RecordClaim(
        string pool,
        RunnerCapacityAvailability availability,
        int attempts,
        long startedAt)
    {
        var elapsedSeconds = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
        NoCtfTelemetry.RecordRunnerClaim(
            pool,
            availability switch
            {
                RunnerCapacityAvailability.Claimed => "claimed",
                RunnerCapacityAvailability.Insufficient => "insufficient",
                RunnerCapacityAvailability.Unavailable => "unavailable",
                _ => "unknown"
            },
            attempts,
            elapsedSeconds);
        NoCtfTelemetry.RecordRedisOperation(
            "runner_claim",
            availability == RunnerCapacityAvailability.Unavailable ? "failure" : "success",
            elapsedSeconds);
    }

    private static void RecordRedisOperation(string endpoint, string outcome, long startedAt) =>
        NoCtfTelemetry.RecordRedisOperation(
            endpoint,
            outcome,
            Stopwatch.GetElapsedTime(startedAt).TotalSeconds);
}
