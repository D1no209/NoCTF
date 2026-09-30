using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.KeyValueStore;
using NATS.Net;
using NoCTF.Application.Observability;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Messaging;

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
    TimeSpan TimeToLive,
    bool HasActiveAssignments,
    bool ProviderAvailable,
    RunnerAdmissionSnapshot Admission,
    RunnerAdmissionOptions AdmissionOptions,
    bool Reconciled = false,
    ulong ResourceDomainFencingToken = 0,
    long ProcessesPerService = 256);

public sealed class NatsRunnerAvailabilityRegistry(
    INatsConnection connection, TimeProvider clock)
{
    private const string Bucket = "NOCTF_RUNNER_AVAILABILITY_V3";

    public async Task PublishHeartbeatAsync(
        string pool, string runnerId, RuntimeProvider provider,
        TimeSpan ttl, CancellationToken ct)
    {
        await EnsurePoolMemberAsync(pool, runnerId, ct);
        var store = await OpenAsync(ct);
        await store.PutAsync(HeartbeatKey(runnerId), Serialize(new RunnerHeartbeat(
            pool, runnerId, provider, clock.GetUtcNow().Add(ttl))),
            cancellationToken: ct);
    }

    public async Task<RunnerAvailabilityRegistrationOutcome> RegisterAsync(
        RunnerAvailabilityRegistration registration, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.RunnerPool);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.RunnerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.Version);
        if (registration.TimeToLive <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(registration));
        await EnsurePoolMemberAsync(registration.RunnerPool, registration.RunnerId, ct);
        var store = await OpenAsync(ct);
        var now = clock.GetUtcNow();
        var row = new RunnerRegistrationState(3, registration, now.Add(registration.TimeToLive));
        await store.PutAsync(RegistrationKey(registration.RunnerId), Serialize(row),
            cancellationToken: ct);
        var outcome = Evaluate(row, now);
        if (outcome == RunnerAvailabilityRegistrationOutcome.Online
            && !await HasCurrentOwnershipAsync(registration, ct))
            outcome = RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted;
        var capacity = registration.Admission.Capacity;
        NoCtfTelemetry.UpdateRunnerCapacitySnapshot(
            registration.RunnerPool, registration.RunnerId,
            outcome == RunnerAvailabilityRegistrationOutcome.Online,
            capacity?.AdmissionAvailable.MemoryBytes ?? 0,
            capacity?.ObservedTotal.MemoryBytes ?? 0,
            capacity?.AdmissionAvailable.CpuMillicores ?? 0,
            capacity?.ObservedTotal.CpuMillicores ?? 0,
            capacity?.AdmissionAvailable.PidsLimit ?? 0,
            capacity?.ObservedTotal.PidsLimit ?? 0);
        return outcome;
    }

    public async Task<RunnerHeartbeatStatus> GetHeartbeatAsync(
        string pool, string runnerId, CancellationToken ct)
    {
        try
        {
            var store = await OpenAsync(ct);
            var heartbeat = await ReadHeartbeatAsync(store, runnerId, ct);
            return heartbeat is not null && heartbeat.Pool == pool
                && heartbeat.ExpiresAt > clock.GetUtcNow()
                ? RunnerHeartbeatStatus.Online : RunnerHeartbeatStatus.Offline;
        }
        catch (NatsException)
        {
            return RunnerHeartbeatStatus.Unavailable;
        }
    }

    public async Task<RunnerPoolInventory> GetPoolInventoryAsync(
        string pool, CancellationToken ct)
    {
        try
        {
            var store = await OpenAsync(ct);
            var index = await ReadPoolAsync(store, pool, ct);
            return new(RunnerPoolInventoryAvailability.Available,
                index.RunnerIds.Order(StringComparer.Ordinal).ToArray());
        }
        catch (NatsException)
        {
            return new(RunnerPoolInventoryAvailability.Unavailable, []);
        }
    }

    public async Task<IReadOnlyList<RunnerAvailabilityRegistration>>
        ReadEligibleAsync(string pool, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "success";
        try
        {
            var store = await OpenAsync(ct);
            var index = await ReadPoolAsync(store, pool, ct);
            var now = clock.GetUtcNow();
            var eligible = new List<RunnerAvailabilityRegistration>();
            foreach (var runnerId in index.RunnerIds)
            {
                var registration = await ReadRegistrationAsync(store, runnerId, ct);
                if (registration is null
                    || registration.Registration.RunnerPool != pool
                    || Evaluate(registration, now) != RunnerAvailabilityRegistrationOutcome.Online)
                    continue;
                var heartbeat = await ReadHeartbeatAsync(store, runnerId, ct);
                if (heartbeat is null || heartbeat.ExpiresAt <= now
                    || heartbeat.Pool != pool) continue;
                if (!await HasCurrentOwnershipAsync(registration.Registration, ct))
                    continue;
                eligible.Add(registration.Registration);
            }
            return eligible;
        }
        catch
        {
            outcome = "failure";
            throw;
        }
        finally
        {
            NoCtfTelemetry.RecordNatsOperation("runner_inventory", outcome,
                Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }

    public async Task<RunnerAvailabilityRegistration?> ReadEligibleRunnerAsync(
        string pool, string runnerId, CancellationToken ct)
    {
        var registration = await ReadEligibleRunnerAsync(runnerId, ct);
        return registration?.RunnerPool == pool ? registration : null;
    }

    public async Task<RunnerAvailabilityRegistration?> ReadEligibleRunnerAsync(
        string runnerId, CancellationToken ct)
    {
        var store = await OpenAsync(ct);
        var now = clock.GetUtcNow();
        var registration = await ReadRegistrationAsync(store, runnerId, ct);
        if (registration is null
            || Evaluate(registration, now) != RunnerAvailabilityRegistrationOutcome.Online)
            return null;
        var heartbeat = await ReadHeartbeatAsync(store, runnerId, ct);
        return heartbeat is not null
            && heartbeat.Pool == registration.Registration.RunnerPool
            && heartbeat.ExpiresAt > now
            && await HasCurrentOwnershipAsync(registration.Registration, ct)
            ? registration.Registration : null;
    }

    private static RunnerAvailabilityRegistrationOutcome Evaluate(
        RunnerRegistrationState row, DateTimeOffset now)
    {
        var registration = row.Registration;
        if (row.Schema != 3 || row.ExpiresAt <= now || !registration.Reconciled
            || registration.ResourceDomainFencingToken == 0)
            return RunnerAvailabilityRegistrationOutcome.OfflineCapacityUntrusted;
        if (!registration.ProviderAvailable)
            return RunnerAvailabilityRegistrationOutcome.OfflineProviderUnavailable;
        if (registration.Admission.State != RunnerAdmissionState.Ready
            || registration.Admission.Capacity is null
            || registration.Admission.Observation is not { } observation
            || observation.ObservedAt > now
            || now - observation.ObservedAt
                > TimeSpan.FromSeconds(registration.AdmissionOptions.FreshnessSeconds))
            return RunnerAvailabilityRegistrationOutcome.OfflineAdmissionBlocked;
        return RunnerAvailabilityRegistrationOutcome.Online;
    }

    private async Task<bool> HasCurrentOwnershipAsync(
        RunnerAvailabilityRegistration registration, CancellationToken ct)
    {
        var domain = registration.Admission.Observation?.ResourceDomain;
        if (string.IsNullOrWhiteSpace(domain)) return false;
        try
        {
            var store = await connection.CreateKeyValueStoreContext()
                .GetStoreAsync("NOCTF_LEASES_V2", ct);
            var entry = await store.GetEntryAsync<string>(
                NatsClusterLeaseManager.ResourceDomainKey(domain),
                cancellationToken: ct);
            return entry.Revision == registration.ResourceDomainFencingToken
                && entry.Value?.StartsWith(registration.RunnerId + "|",
                    StringComparison.Ordinal) == true;
        }
        catch (NatsKVException) { return false; }
    }

    private async Task EnsurePoolMemberAsync(string pool, string runnerId, CancellationToken ct)
    {
        var store = await OpenAsync(ct);
        var key = PoolKey(pool);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var current = await ReadEntryAsync(store, key, ct);
            var index = current is null
                ? new RunnerPoolIndex([])
                : JsonSerializer.Deserialize(current.Value.Value,
                    RunnerAvailabilityJsonContext.Default.RunnerPoolIndex)
                    ?? throw new InvalidOperationException("Invalid Runner pool index.");
            if (index.RunnerIds.Contains(runnerId, StringComparer.Ordinal))
                return;
            var next = new RunnerPoolIndex([.. index.RunnerIds, runnerId]);
            try
            {
                if (current is null)
                    await store.CreateAsync(key, Serialize(next), cancellationToken: ct);
                else
                    await store.UpdateAsync(key, Serialize(next),
                        current.Value.Revision, cancellationToken: ct);
                return;
            }
            catch (NatsKVException exception) when (attempt < 7
                && exception is NatsKVCreateException or NatsKVWrongLastRevisionException)
            {
                await Task.Delay(Random.Shared.Next(1, 9), ct);
            }
        }
        throw new InvalidOperationException("Runner pool index contention exceeded the retry limit.");
    }

    private async Task<RunnerPoolIndex> ReadPoolAsync(
        INatsKVStore store, string pool, CancellationToken ct)
    {
        var entry = await ReadEntryAsync(store, PoolKey(pool), ct);
        return entry is null ? new([])
            : JsonSerializer.Deserialize(entry.Value.Value,
                RunnerAvailabilityJsonContext.Default.RunnerPoolIndex)
                ?? throw new InvalidOperationException("Invalid Runner pool index.");
    }

    private static async Task<RunnerHeartbeat?> ReadHeartbeatAsync(
        INatsKVStore store, string runnerId, CancellationToken ct)
    {
        var entry = await ReadEntryAsync(store, HeartbeatKey(runnerId), ct);
        return entry is null ? null
            : JsonSerializer.Deserialize(entry.Value.Value,
                RunnerAvailabilityJsonContext.Default.RunnerHeartbeat);
    }

    private static async Task<RunnerRegistrationState?> ReadRegistrationAsync(
        INatsKVStore store, string runnerId, CancellationToken ct)
    {
        var entry = await ReadEntryAsync(store, RegistrationKey(runnerId), ct);
        return entry is null ? null
            : JsonSerializer.Deserialize(entry.Value.Value,
                RunnerAvailabilityJsonContext.Default.RunnerRegistrationState);
    }

    private static async Task<(byte[] Value, ulong Revision)?> ReadEntryAsync(
        INatsKVStore store, string key, CancellationToken ct)
    {
        try
        {
            var entry = await store.GetEntryAsync<byte[]>(key, cancellationToken: ct);
            return entry.Value is null ? null : (entry.Value, entry.Revision);
        }
        catch (NatsKVKeyNotFoundException) { return null; }
        catch (NatsKVKeyDeletedException) { return null; }
    }

    private async Task<INatsKVStore> OpenAsync(CancellationToken ct) =>
        await connection.CreateKeyValueStoreContext().CreateOrUpdateStoreAsync(
            new NatsKVConfig(Bucket)
            {
                Description = "NoCTF Runner schema 3 online registrations",
                History = 1
            }, ct);

    private static string PoolKey(string pool) => "pool." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pool)))
            .ToLowerInvariant();
    private static string HeartbeatKey(string runnerId) => "heartbeat." + runnerId;
    private static string RegistrationKey(string runnerId) => "registration." + runnerId;
    private static byte[] Serialize(RunnerPoolIndex value) =>
        JsonSerializer.SerializeToUtf8Bytes(value,
            RunnerAvailabilityJsonContext.Default.RunnerPoolIndex);
    private static byte[] Serialize(RunnerHeartbeat value) =>
        JsonSerializer.SerializeToUtf8Bytes(value,
            RunnerAvailabilityJsonContext.Default.RunnerHeartbeat);
    private static byte[] Serialize(RunnerRegistrationState value) =>
        JsonSerializer.SerializeToUtf8Bytes(value,
            RunnerAvailabilityJsonContext.Default.RunnerRegistrationState);
}

internal sealed record RunnerPoolIndex(string[] RunnerIds);
internal sealed record RunnerHeartbeat(
    string Pool, string RunnerId, RuntimeProvider Provider, DateTimeOffset ExpiresAt);
internal sealed record RunnerRegistrationState(
    int Schema, RunnerAvailabilityRegistration Registration, DateTimeOffset ExpiresAt);

[JsonSerializable(typeof(RunnerPoolIndex))]
[JsonSerializable(typeof(RunnerHeartbeat))]
[JsonSerializable(typeof(RunnerRegistrationState))]
internal partial class RunnerAvailabilityJsonContext : JsonSerializerContext;
