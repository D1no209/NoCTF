using System.Text.Json;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Runtime.Capacity;

public sealed class RedisRunnerCapacityLedger(IConnectionMultiplexer redis, TimeProvider? clock = null)
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    public async Task<IReadOnlyList<RuntimeWorkloadIdentity>> ReadUnconfirmedAsync(string runnerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var keys = await redis.GetDatabase().SortedSetRangeByRankAsync($"runner:{runnerId}:unconfirmed-claims", 0, 127);
        var result = new List<RuntimeWorkloadIdentity>(keys.Length);
        foreach (var key in keys)
        {
            var parts = key.ToString().Split(':');
            if (parts.Length != 4 || parts[0] != "runner-claim" || !short.TryParse(parts[1], out var kind)
                || !Guid.TryParseExact(parts[2], "N", out var runtimeId) || !Guid.TryParseExact(parts[3], "N", out var operationId))
                throw new InvalidOperationException("Invalid unconfirmed capacity identity.");
            var identity = new RuntimeWorkloadIdentity((RuntimeWorkloadKind)kind, runtimeId, operationId);
            identity.Validate();
            result.Add(identity);
        }
        return result;
    }

    public async Task ConfirmAsync(string runnerId, RuntimeWorkloadIdentity identity, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await redis.GetDatabase().SortedSetRemoveAsync($"runner:{runnerId}:unconfirmed-claims", $"runner-claim:{identity.Key}");
    }

    public async Task DeferConfirmationAsync(string runnerId, RuntimeWorkloadIdentity identity, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Rotate uncertain entries so they cannot starve later unconfirmed claims.
        await redis.GetDatabase().SortedSetAddAsync($"runner:{runnerId}:unconfirmed-claims", $"runner-claim:{identity.Key}",
            timeProvider.GetUtcNow().ToUnixTimeMilliseconds(), When.Exists);
    }

    public async Task<RuntimeCapacityAllocation?> ReadLegacyAsync(RuntimeInstance runtime, string runnerId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var entries = (await redis.GetDatabase().HashGetAllAsync($"runner-claim:{runtime.Id:N}"))
            .ToDictionary(entry => entry.Name.ToString(), entry => entry.Value.ToString(), StringComparer.Ordinal);
        if (entries.GetValueOrDefault("runnerId") != runnerId
            || !long.TryParse(entries.GetValueOrDefault("memoryBytes"), out var memory)
            || !long.TryParse(entries.GetValueOrDefault("nanoCpus"), out var cpu)
            || !long.TryParse(entries.GetValueOrDefault("pidsLimit"), out var pids)
            || memory <= 0 || cpu <= 0 || pids <= 0)
            return null;
        var amount = new RuntimeResourceAmount(memory, cpu, pids);
        return new(PersistedRunnerCapacityGate.PrimaryIdentity(runtime), runtime.GameplayFactId,
            runnerId, runnerId, amount, amount);
    }

    public async Task PauseAsync(string runnerId, string pool, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await redis.GetDatabase().ScriptEvaluateAsync("""
            redis.call('HSET', KEYS[1], 'admissionState', 'reconciling')
            redis.call('ZREM', KEYS[2], ARGV[1])
            return 1
            """, [$"runner:{runnerId}:capacity", $"runner-pool:{pool}:candidates"], [runnerId]);
    }

    public async Task RestoreAsync(string runnerId, string pool, RunnerCapacityProjection projection,
        DateTimeOffset observedAt,
        IReadOnlyList<RuntimeCapacityAllocation> allocations, CancellationToken ct,
        IReadOnlySet<RuntimeWorkloadIdentity>? starting = null,
        IReadOnlyList<string>? claimKeys = null)
    {
        foreach (var allocation in allocations)
        {
            allocation.Validate();
            if (allocation.RunnerId != runnerId)
                throw new InvalidOperationException("Cannot rebuild another Runner's allocation.");
        }
        var database = redis.GetDatabase();
        var keys = claimKeys ?? await ReadClaimKeysAsync(runnerId, ct);
        var rows = allocations.Select(item => new
        {
            key = $"runner-claim:{item.Identity.Key}", memory = item.Limit.MemoryBytes,
            cpu = item.Limit.NanoCpus, pids = item.Limit.PidsLimit,
            auxiliary = item.Identity.IsAuxiliary,
            starting = starting?.Contains(item.Identity) == true
        }).ToArray();
        var restored = (long)await database.ScriptEvaluateAsync("""
            if redis.call('HGET', KEYS[1], 'admissionState') ~= 'reconciling' then return 0 end
            local old = cjson.decode(ARGV[6])
            for _, key in ipairs(old) do
                if redis.call('HGET', key, 'runnerId') == ARGV[1] then redis.call('DEL', key) end
            end
            local rows = cjson.decode(ARGV[7])
            local primary = 0
            local auxiliary = 0
            local reservedMemory = 0
            local reservedCpu = 0
            local reservedPids = 0
            for _, row in ipairs(rows) do
                redis.call('HSET', row.key, 'runnerId', ARGV[1], 'pool', ARGV[2],
                    'memoryBytes', row.memory, 'nanoCpus', row.cpu, 'pidsLimit', row.pids,
                    'starting', row.starting and 1 or 0, 'auxiliary', row.auxiliary and 1 or 0,
                    'acquiredObservedAt', ARGV[17])
                if row.starting then
                    reservedMemory = reservedMemory + row.memory
                    reservedCpu = reservedCpu + row.cpu
                    reservedPids = reservedPids + row.pids
                    if row.auxiliary then auxiliary = auxiliary + 1 else primary = primary + 1 end
                end
            end
            local admissionMemory = math.max(0, tonumber(ARGV[10]) - tonumber(ARGV[13]) - reservedMemory)
            local admissionCpu = math.max(0, tonumber(ARGV[11]) - tonumber(ARGV[14]) - reservedCpu)
            local admissionPids = math.max(0, tonumber(ARGV[12]) - tonumber(ARGV[15]) - reservedPids)
            redis.call('HSET', KEYS[1], 'registrationSchema', '3', 'admissionState', 'starting',
                'startingPrimary', primary, 'startingAuxiliary', auxiliary,
                'startupReservedMemoryBytes', reservedMemory, 'startupReservedNanoCpus', reservedCpu,
                'startupReservedPids', reservedPids,
                'observedTotalMemoryBytes', ARGV[3], 'observedTotalNanoCpus', ARGV[4], 'observedTotalPids', ARGV[5],
                'observedAvailableMemoryBytes', ARGV[10], 'observedAvailableNanoCpus', ARGV[11], 'observedAvailablePids', ARGV[12],
                'safetyHeadroomMemoryBytes', ARGV[13], 'safetyHeadroomNanoCpus', ARGV[14], 'safetyHeadroomPids', ARGV[15],
                'admissionAvailableMemoryBytes', admissionMemory, 'admissionAvailableNanoCpus', admissionCpu,
                'admissionAvailablePids', admissionPids, 'pidsObserved', ARGV[16])
            redis.call('PERSIST', KEYS[1])
            redis.call('DEL', 'runner:' .. ARGV[1] .. ':unconfirmed-claims')
            redis.call('DEL', 'runner:' .. ARGV[1] .. ':completed-startups')
            redis.call('ZREM', KEYS[2], ARGV[1])
            return 1
            """, [$"runner:{runnerId}:capacity", $"runner-pool:{pool}:candidates"],
            [runnerId, pool, projection.ObservedTotal.MemoryBytes, projection.ObservedTotal.NanoCpus,
                projection.ObservedTotal.PidsLimit ?? 0, JsonSerializer.Serialize(keys), JsonSerializer.Serialize(rows),
                0, 0, projection.ObservedAvailable.MemoryBytes, projection.ObservedAvailable.NanoCpus,
                projection.ObservedAvailable.PidsLimit ?? 0, projection.SafetyHeadroom.MemoryBytes,
                projection.SafetyHeadroom.NanoCpus, projection.SafetyHeadroom.PidsLimit ?? 0,
                projection.ObservedTotal.PidsLimit is null ? 0 : 1, observedAt.ToUnixTimeMilliseconds()]);
        if (restored != 1)
            throw new InvalidOperationException("Capacity recovery lost its admission barrier.");
    }

    public async Task RestoreLegacyAsync(string runnerId, string pool, RuntimeResourceLimits total,
        IReadOnlyList<RuntimeCapacityAllocation> allocations, CancellationToken ct,
        IReadOnlySet<RuntimeWorkloadIdentity>? starting = null,
        IReadOnlyList<string>? claimKeys = null)
    {
        foreach (var allocation in allocations)
        {
            allocation.Validate();
            if (allocation.RunnerId != runnerId)
                throw new InvalidOperationException("Cannot rebuild another Runner's allocation.");
        }
        var database = redis.GetDatabase();
        var keys = claimKeys ?? await ReadClaimKeysAsync(runnerId, ct);
        var rows = allocations.Select(item => new
        {
            key = $"runner-claim:{item.Identity.Key}", memory = item.Budget.MemoryBytes,
            cpu = item.Budget.NanoCpus, pids = item.Budget.PidsLimit,
            auxiliary = item.Identity.IsAuxiliary,
            starting = item.Identity.IsAuxiliary || starting?.Contains(item.Identity) == true
        }).ToArray();
        var restored = (long)await database.ScriptEvaluateAsync("""
            if redis.call('HGET', KEYS[1], 'admissionState') ~= 'reconciling' then return 0 end
            local old = cjson.decode(ARGV[6])
            for _, key in ipairs(old) do
                if redis.call('HGET', key, 'runnerId') == ARGV[1] then redis.call('DEL', key) end
            end
            local memory = tonumber(ARGV[3])
            local cpu = tonumber(ARGV[4])
            local pids = tonumber(ARGV[5])
            local rows = cjson.decode(ARGV[7])
            local primary = 0
            local auxiliary = 0
            for _, row in ipairs(rows) do
                memory = memory - row.memory
                cpu = cpu - row.cpu
                pids = pids - row.pids
                redis.call('HSET', row.key, 'runnerId', ARGV[1], 'pool', ARGV[2],
                    'memoryBytes', row.memory, 'nanoCpus', row.cpu, 'pidsLimit', row.pids,
                    'starting', row.starting and 1 or 0, 'auxiliary', row.auxiliary and 1 or 0)
                if row.starting then
                    if row.auxiliary then auxiliary = auxiliary + 1 else primary = primary + 1 end
                end
            end
            redis.call('HSET', KEYS[1], 'registrationSchema', '2', 'admissionState', 'starting',
                'startingPrimary', primary, 'activeAuxiliary', auxiliary,
                'totalMemoryBytes', ARGV[3], 'totalNanoCpus', ARGV[4], 'totalPids', ARGV[5],
                'availableMemoryBytes', memory, 'availableNanoCpus', cpu, 'availablePids', pids)
            redis.call('PERSIST', KEYS[1])
            redis.call('DEL', 'runner:' .. ARGV[1] .. ':unconfirmed-claims')
            redis.call('ZREM', KEYS[2], ARGV[1])
            return 1
            """, [$"runner:{runnerId}:capacity", $"runner-pool:{pool}:candidates"],
            [runnerId, pool, total.MemoryBytes, total.NanoCpus, total.PidsLimit,
                JsonSerializer.Serialize(keys), JsonSerializer.Serialize(rows)]);
        if (restored != 1)
            throw new InvalidOperationException("Legacy capacity recovery lost its admission barrier.");
    }

    // Admission must be closed before this inventory and remain closed through RestoreAsync.
    // Scan outside the PostgreSQL transaction, including pre-index legacy claims.
    public async Task<IReadOnlyList<string>> ReadClaimKeysAsync(string runnerId, CancellationToken ct)
    {
        var database = redis.GetDatabase();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var scanned = 0;
        foreach (var endpoint in redis.GetEndPoints())
        {
            var server = redis.GetServer(endpoint);
            if (server.IsReplica) continue;
            await foreach (var key in server.KeysAsync(database.Database, "runner-claim:*", pageSize: 256)
                               .WithCancellation(ct))
            {
                if (++scanned > 10000)
                    throw new InvalidOperationException("Capacity recovery exceeds its bounded inventory.");
                if ((string?)await database.HashGetAsync(key, "runnerId") == runnerId)
                    keys.Add(key.ToString());
            }
        }
        return keys.ToArray();
    }
}
