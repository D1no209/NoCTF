using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using Npgsql;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Runtime.Capacity;

public sealed class RedisRunnerCapacityDiagnostics(NoCtfDbContext db, IConnectionMultiplexer? redis, TimeProvider clock)
    : IRunnerCapacityDiagnostics
{
    public async Task<RunnerCapacityReport> ReadAsync(CancellationToken ct)
    {
        if (redis is null || !db.Database.IsRelational()) return new(false, []);
        try
        {
            var cache = redis.GetDatabase();
            var members = await cache.SetMembersAsync("runner-registry:nodes");
            var ids = members.Select(member => member.ToString()).Order(StringComparer.Ordinal).Take(128).ToArray();
            if (ids.Length == 0) return new(true, []);
            var totals = await db.Database.SqlQuery<AllocationTotal>($"""
                SELECT item->>'runnerId' AS runner_id,
                       sum((item->'limit'->>'memoryBytes')::bigint)::bigint AS memory_bytes,
                       sum((item->'limit'->>'nanoCpus')::bigint)::bigint AS nano_cpus,
                       sum((item->'limit'->>'pidsLimit')::bigint)::bigint AS pids_limit
                FROM runtime_instances runtime
                CROSS JOIN LATERAL jsonb_array_elements(runtime.capacity_allocations->'items') item
                WHERE runtime.runner_id = ANY({ids})
                GROUP BY item->>'runnerId'
                """).ToDictionaryAsync(row => row.RunnerId, ct);
            var unresolved = (await db.Database.SqlQuery<string>($"""
                SELECT DISTINCT runner_id AS "Value" FROM runtime_instances
                WHERE runner_id = ANY({ids}) AND state IN (1, 2, 3)
                  AND jsonb_array_length(capacity_allocations->'items') = 0
                """).ToArrayAsync(ct)).ToHashSet(StringComparer.Ordinal);
            var snapshots = new List<RunnerCapacitySnapshot>(ids.Length);
            foreach (var runnerId in ids)
            {
                ct.ThrowIfCancellationRequested();
                var fields = (await cache.HashGetAllAsync($"runner:{runnerId}:capacity"))
                    .ToDictionary(field => field.Name.ToString(), field => field.Value.ToString(), StringComparer.Ordinal);
                var alive = await cache.KeyExistsAsync($"runner:{runnerId}:heartbeat");
                var total = Amount(fields, "total");
                var available = Amount(fields, "available");
                var budget = total is not null && available is not null
                    ? new RuntimeResourceAmount(total.MemoryBytes - available.MemoryBytes, total.NanoCpus - available.NanoCpus,
                        total.PidsLimit - available.PidsLimit) : null;
                RunnerAdmissionSnapshot? admission = null;
                if (fields.TryGetValue("observation", out var json))
                {
                    try { admission = JsonSerializer.Deserialize<RunnerAdmissionSnapshot>(json); }
                    catch (JsonException) { }
                }
                var state = fields.GetValueOrDefault("admissionState") switch
                {
                    "ready" => RunnerAdmissionState.Ready,
                    "reconciling" => RunnerAdmissionState.Reconciling,
                    "pressureBlocked" => RunnerAdmissionState.PressureBlocked,
                    "providerUnavailable" => RunnerAdmissionState.ProviderUnavailable,
                    "draining" => RunnerAdmissionState.Draining,
                    _ => RunnerAdmissionState.Starting
                };
                var failure = admission?.Failure;
                if (!alive) { state = RunnerAdmissionState.Starting; failure = RunnerAdmissionFailure.NoEligibleRunner; }
                else if (state == RunnerAdmissionState.Reconciling) failure = RunnerAdmissionFailure.LedgerRecovering;
                else if (state == RunnerAdmissionState.ProviderUnavailable) failure = RunnerAdmissionFailure.ProviderUnavailable;
                else if (fields.GetValueOrDefault("observationRequired") == "1"
                    && (!long.TryParse(fields.GetValueOrDefault("observationFreshUntil"), out var untilAt)
                        || untilAt < clock.GetUtcNow().ToUnixTimeMilliseconds()))
                {
                    state = RunnerAdmissionState.Starting;
                    failure = RunnerAdmissionFailure.ObservationStale;
                }
                RuntimeResourceAmount? limit = unresolved.Contains(runnerId) ? null
                    : totals.TryGetValue(runnerId, out var row)
                        ? new RuntimeResourceAmount(row.MemoryBytes, row.NanoCpus, row.PidsLimit) : new(0, 0, 0);
                snapshots.Add(new(runnerId, fields.GetValueOrDefault("resourceDomain"), alive, state, failure,
                    total, available, budget, limit, admission?.Observation,
                    ReadCount(fields, "startingPrimary"), ReadCount(fields, "activeAuxiliary")));
            }
            return new(true, snapshots, members.Length > ids.Length);
        }
        catch (Exception exception) when (exception is RedisException or NpgsqlException)
        {
            return new(false, []);
        }
    }

    private static int? ReadCount(IReadOnlyDictionary<string, string> fields, string key) =>
        int.TryParse(fields.GetValueOrDefault(key), out var count) ? count : null;

    private static RuntimeResourceAmount? Amount(IReadOnlyDictionary<string, string> fields, string prefix) =>
        long.TryParse(fields.GetValueOrDefault(prefix + "MemoryBytes"), out var memory)
        && long.TryParse(fields.GetValueOrDefault(prefix + "NanoCpus"), out var cpu)
        && long.TryParse(fields.GetValueOrDefault(prefix + "Pids"), out var pids) ? new(memory, cpu, pids) : null;

    private sealed record AllocationTotal(string RunnerId, long MemoryBytes, long NanoCpus, long PidsLimit);
}
