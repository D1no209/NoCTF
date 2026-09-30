using System.Data;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;
using NoCTF.Application.Messaging;
using NoCTF.Application.Observability;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Persistence;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Infrastructure.Runtime.Capacity;

/// <summary>EF allocations are authoritative; NATS only advertises fresh Runner observations.</summary>
public sealed class PersistedRunnerCapacityGate(
    NoCtfDbContext db,
    NatsRunnerAvailabilityRegistry runners,
    IFusionCacheProvider caches,
    IPostCommitMessagePublisher outbox,
    RunnerCapacityLedgerCoordinator ledgerCoordinator) : IRunnerCapacityGate
{
    private readonly IFusionCache waiting = caches.GetCache(NoCtfCacheNames.ReadModels);

    public async Task RecordWaitingAsync(Guid runtimeId,
        RunnerAdmissionFailure? failure, CancellationToken ct)
    {
        if (failure is null)
            await waiting.RemoveAsync(WaitingKey(runtimeId), token: ct);
        else
            await waiting.SetAsync(WaitingKey(runtimeId), failure.Value, token: ct);
    }

    public async Task<IReadOnlyDictionary<Guid, RunnerAdmissionFailure>> ReadWaitingAsync(
        IReadOnlyList<Guid> runtimeIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, RunnerAdmissionFailure>();
        foreach (var runtimeId in runtimeIds.Distinct())
        {
            var failure = await waiting.GetOrDefaultAsync<RunnerAdmissionFailure?>(
                WaitingKey(runtimeId), null, token: ct);
            if (failure is not null) result[runtimeId] = failure.Value;
        }
        return result;
    }

    public Task<RunnerHeartbeatStatus> GetHeartbeatAsync(
        string pool, string runnerId, CancellationToken ct) =>
        runners.GetHeartbeatAsync(pool, runnerId, ct);

    public Task<RunnerPoolInventory> GetPoolInventoryAsync(
        string pool, CancellationToken ct) => runners.GetPoolInventoryAsync(pool, ct);

    public Task CompleteStartupAsync(Guid runtimeInstanceId, string runnerId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // RuntimeState.Running removes the startup reservation from subsequent EF reads.
        return Task.CompletedTask;
    }

    public async Task<bool> CanCreateAsync(Guid runtimeInstanceId,
        string runnerId, CancellationToken ct)
    {
        var runtime = await db.RuntimeInstances.AsNoTracking()
            .Include(item => item.CapacityAllocationEntries)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == runtimeInstanceId
                && item.State == RuntimeState.Provisioning && item.RunnerId == runnerId, ct);
        var allocation = runtime?.CapacityAllocations.Items.SingleOrDefault(
            item => !item.Identity.IsAuxiliary && item.RunnerId == runnerId);
        return allocation is not null && await IsReadyOwnerAsync(allocation, ct);
    }

    public async Task<bool> CanCreateWorkloadAsync(
        RuntimeWorkloadIdentity identity, Guid factId, string runnerId,
        CancellationToken ct)
    {
        var runtime = await db.RuntimeInstances.AsNoTracking()
            .Include(item => item.CapacityAllocationEntries)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == identity.RuntimeInstanceId
                && item.RunnerId == runnerId && item.State == RuntimeState.Running, ct);
        var allocation = runtime?.CapacityAllocations.Items.SingleOrDefault(item =>
            item.Identity == identity && item.GameplayFactId == factId
            && item.RunnerId == runnerId);
        return allocation is not null
            && await db.GameplayFacts.AnyAsync(fact => fact.Id == factId
                && fact.State == GameplayFactState.Processing, ct)
            && await IsReadyOwnerAsync(allocation, ct);
    }

    private async Task<bool> IsReadyOwnerAsync(RuntimeCapacityAllocation allocation,
        CancellationToken ct)
    {
        try
        {
            var registration = await runners.ReadEligibleRunnerAsync(
                allocation.RunnerId, ct);
            return registration?.Admission.Observation?.ResourceDomain
                == allocation.ResourceDomain;
        }
        catch (NatsException) { return false; }
    }

    public Task<RunnerCapacityClaim> TryClaimAsync(
        RunnerCapacityRequest request, CancellationToken ct) =>
        ClaimWithRetriesAsync(request, null, ct);

    public Task<RunnerCapacityClaim> TryClaimForRunnerAsync(
        RunnerCapacityRequest request, string runnerId, CancellationToken ct) =>
        ClaimWithRetriesAsync(request, runnerId, ct);

    private async Task<RunnerCapacityClaim> ClaimWithRetriesAsync(
        RunnerCapacityRequest request, string? runnerId, CancellationToken ct)
    {
        var hasAmbientTransaction = db.Database.CurrentTransaction is not null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return await ClaimAsync(request, runnerId, ct); }
            catch (Exception exception) when (!hasAmbientTransaction && attempt < 2
                && IsTransactionFailure(exception))
            {
                NoCtfTelemetry.RecordRunnerCapacityTransactionRetry(
                    RunnerCapacityTransactionOperation.Claim);
                db.ChangeTracker.Clear();
                await Task.Delay(Random.Shared.Next(2, 18), ct);
            }
            catch (Exception exception) when (!hasAmbientTransaction
                && IsTransactionFailure(exception))
            {
                NoCtfTelemetry.RecordRunnerCapacityTransactionExhaustion(
                    RunnerCapacityTransactionOperation.Claim);
                throw;
            }
        }
        throw new InvalidOperationException("Runner capacity retries were exhausted.");
    }

    private async Task<RunnerCapacityClaim> ClaimAsync(
        RunnerCapacityRequest request, string? requestedRunnerId, CancellationToken ct)
    {
        ValidateRequest(request);
        var started = Stopwatch.GetTimestamp();
        var availability = RunnerCapacityAvailability.Unavailable;
        var attempts = 0;
        try
        {
            IReadOnlyList<RunnerAvailabilityRegistration> eligible;
            try
            {
                eligible = requestedRunnerId is null
                    ? await runners.ReadEligibleAsync(request.Pool, ct)
                    : await runners.ReadEligibleRunnerAsync(
                        request.Pool, requestedRunnerId, ct) is { } selected
                        ? [selected] : [];
            }
            catch (NatsException)
            {
                return new(RunnerCapacityAvailability.Unavailable);
            }

            eligible = eligible.Where(candidate => candidate.ProcessesPerService == request.ProcessesPerService).ToArray();
            using var ledgerLease = await ledgerCoordinator.EnterAsync(ct);
            await using var transaction = db.Database.CurrentTransaction is null
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                : null;
            var ledger = await db.RuntimeCapacityLedgers.SingleAsync(
                item => item.Id == 1, ct);
            var current = await db.RuntimeInstances.AsNoTracking()
                .Where(item => item.Id == request.RuntimeInstanceId)
                .Select(item => new { item.State, item.RunnerId })
                .SingleOrDefaultAsync(ct);
            var runtime = await db.RuntimeInstances
                .Include(item => item.CapacityAllocationEntries)
                .AsSplitQuery()
                .SingleOrDefaultAsync(item => item.Id == request.RuntimeInstanceId, ct);
            if (runtime is null || current is null
                || current.State is RuntimeState.Stopped or RuntimeState.Stopping
                    or RuntimeState.Failed)
                return new(RunnerCapacityAvailability.Unavailable,
                    Failure: RunnerAdmissionFailure.NoEligibleRunner);

            var identity = request.Workload ?? PrimaryIdentity(runtime);
            identity.Validate();
            if (identity.RuntimeInstanceId != runtime.Id)
                throw new InvalidOperationException(
                    "The capacity identity belongs to another Runtime.");
            if (!identity.IsAuxiliary && identity != PrimaryIdentity(runtime)
                || current.RunnerId is { } owner && requestedRunnerId is not null
                    && owner != requestedRunnerId)
                return new(RunnerCapacityAvailability.Unavailable,
                    Failure: RunnerAdmissionFailure.NoEligibleRunner);
            if (identity.IsAuxiliary && (request.GameplayFactId is not Guid factId
                || !await db.GameplayFacts.AnyAsync(fact => fact.Id == factId
                    && fact.State == GameplayFactState.Processing
                    && fact.CompetitionChallengeId == runtime.CompetitionChallengeId
                    && fact.TeamId == runtime.TeamId
                    && (identity.Kind == RuntimeWorkloadKind.AwdChecker
                        && fact.Kind == GameplayFactKind.AwdServiceTransition
                        || identity.Kind == RuntimeWorkloadKind.PatchChecker
                            && runtime.GameplayFactId == fact.Id), ct)))
                return new(RunnerCapacityAvailability.Unavailable,
                    Failure: RunnerAdmissionFailure.NoEligibleRunner);

            var existing = runtime.CapacityAllocations.Items.SingleOrDefault(
                item => item.Identity == identity);
            var limit = existing?.Limit ?? request.Limit
                ?? new RuntimeResourceAmount(
                    request.MemoryBytes, request.CpuMillicores, request.PidsLimit);
            var budget = existing?.Budget ?? new RuntimeResourceAmount(
                request.MemoryBytes, request.CpuMillicores, request.PidsLimit);
            limit.Validate();
            budget.Validate();
            if (existing is not null)
            {
                if (requestedRunnerId is not null
                    && existing.RunnerId != requestedRunnerId)
                    return new(RunnerCapacityAvailability.Unavailable,
                        Failure: RunnerAdmissionFailure.NoEligibleRunner);
                var previousOwner = eligible.FirstOrDefault(item =>
                    item.RunnerId == existing.RunnerId
                    && item.Admission.Observation?.ResourceDomain
                        == existing.ResourceDomain);
                return previousOwner is null
                    ? new(RunnerCapacityAvailability.Unavailable,
                        Failure: RunnerAdmissionFailure.NoEligibleRunner)
                    : new(RunnerCapacityAvailability.Claimed,
                        existing.RunnerId, RunnerCapacityClaimState.AlreadyOwned);
            }

            var candidates = eligible.Where(item => current.RunnerId is null
                    || item.RunnerId == current.RunnerId)
                .OrderBy(AdmissionPressure)
                .ThenBy(item => item.RunnerId, StringComparer.Ordinal)
                .Take(8).ToArray();
            var candidateIds = candidates.Select(item => item.RunnerId).ToArray();
            var allocatedRuntimes = candidateIds.Length == 0 ? []
                : await db.RuntimeInstances.AsNoTracking().IgnoreAutoIncludes()
                    .Include(item => item.CapacityAllocationEntries)
                    .Where(item => item.CapacityAllocationEntries.Any(allocation =>
                        candidateIds.Contains(allocation.RunnerId)))
                    .AsSplitQuery().ToArrayAsync(ct);
            var auxiliaryFactIds = allocatedRuntimes
                .SelectMany(item => item.CapacityAllocations.Items)
                .Where(item => item.Identity.IsAuxiliary
                    && item.GameplayFactId is not null)
                .Select(item => item.GameplayFactId!.Value).Distinct().ToArray();
            var processingFacts = auxiliaryFactIds.Length == 0
                ? new HashSet<Guid>()
                : (await db.GameplayFacts.AsNoTracking()
                    .Where(fact => auxiliaryFactIds.Contains(fact.Id)
                        && fact.State == GameplayFactState.Processing)
                    .Select(fact => fact.Id).ToArrayAsync(ct)).ToHashSet();
            RunnerAdmissionFailure? firstFailure = null;
            foreach (var candidate in candidates)
            {
                attempts++;
                var reservations = allocatedRuntimes.SelectMany(item =>
                    item.CapacityAllocations.Items.Where(allocation =>
                        allocation.RunnerId == candidate.RunnerId
                        && (!allocation.Identity.IsAuxiliary
                            && (item.State != RuntimeState.Running
                                || item.RunningAt is null
                                || candidate.Admission.Observation!.ObservedAt
                                    < item.RunningAt.Value)
                            || allocation.Identity.IsAuxiliary
                                && allocation.GameplayFactId is Guid factId
                                && processingFacts.Contains(factId))))
                    .ToArray();
                var failure = CheckCapacity(candidate, limit, reservations, identity.IsAuxiliary);
                if (failure is not null)
                {
                    firstFailure ??= failure;
                    continue;
                }
                var domain = candidate.Admission.Observation!.ResourceDomain;
                var entry = RuntimeCapacityAllocationEntry.FromValue(new(
                    identity, request.GameplayFactId, domain,
                    candidate.RunnerId, budget, limit));
                runtime.CapacityAllocationEntries.Add(entry);
                db.Entry(entry).State = EntityState.Added;
                ledger.ConcurrencyStamp = Guid.NewGuid();
                await db.SaveChangesAsync(ct);
                if (transaction is not null)
                    await transaction.CommitAsync(ct);
                availability = RunnerCapacityAvailability.Claimed;
                return new(availability, candidate.RunnerId,
                    RunnerCapacityClaimState.Acquired);
            }
            availability = RunnerCapacityAvailability.Insufficient;
            return new(availability, Failure: firstFailure
                ?? RunnerAdmissionFailure.NoEligibleRunner);
        }
        finally
        {
            NoCtfTelemetry.RecordRunnerClaim(request.Pool,
                availability switch
                {
                    RunnerCapacityAvailability.Claimed => "claimed",
                    RunnerCapacityAvailability.Insufficient => "insufficient",
                    _ => "unavailable"
                }, Math.Max(1, attempts),
                Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }

    private static RunnerAdmissionFailure? CheckCapacity(
        RunnerAvailabilityRegistration candidate,
        RuntimeResourceAmount requested,
        IReadOnlyCollection<RuntimeCapacityAllocation> reservations,
        bool auxiliary)
    {
        var projection = candidate.Admission.Capacity!;
        if (requested.MemoryBytes > projection.ObservedTotal.MemoryBytes
            || requested.CpuMillicores > projection.ObservedTotal.CpuMillicores
            || projection.ObservedTotal.PidsLimit is long totalPids
                && requested.PidsLimit > totalPids)
            return RunnerAdmissionFailure.RequestExceedsNodeCapacity;
        var starting = reservations.Count(item => item.Identity.IsAuxiliary == auxiliary);
        var maximum = auxiliary
            ? candidate.AdmissionOptions.AuxiliaryConcurrency
            : candidate.AdmissionOptions.MainStartupConcurrency;
        if (starting >= maximum)
            return RunnerAdmissionFailure.StartupConcurrencyLimited;
        var reservedMemory = reservations.Sum(item => item.Limit.MemoryBytes);
        var reservedCpu = reservations.Sum(item => item.Limit.CpuMillicores);
        var reservedPids = reservations.Sum(item => item.Limit.PidsLimit);
        if (requested.MemoryBytes > Math.Max(0,
                projection.AdmissionAvailable.MemoryBytes - reservedMemory))
            return RunnerAdmissionFailure.MemoryActualCapacityInsufficient;
        if (requested.CpuMillicores > Math.Max(0,
                projection.AdmissionAvailable.CpuMillicores - reservedCpu))
            return RunnerAdmissionFailure.CpuActualCapacityInsufficient;
        if (projection.AdmissionAvailable.PidsLimit is long availablePids
            && requested.PidsLimit > Math.Max(0, availablePids - reservedPids))
            return RunnerAdmissionFailure.PidActualCapacityInsufficient;
        return null;
    }

    private static double AdmissionPressure(RunnerAvailabilityRegistration registration)
    {
        var capacity = registration.Admission.Capacity!;
        return Math.Max(
            1d - (double)capacity.AdmissionAvailable.MemoryBytes
                / Math.Max(1, capacity.ObservedTotal.MemoryBytes),
            1d - (double)capacity.AdmissionAvailable.CpuMillicores
                / Math.Max(1, capacity.ObservedTotal.CpuMillicores));
    }

    private static void ValidateRequest(RunnerCapacityRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Pool);
        if (request.RuntimeInstanceId == Guid.Empty || request.MemoryBytes <= 0
            || request.CpuMillicores <= 0 || request.PidsLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.Workload is { } identity)
        {
            identity.Validate();
            if (identity.RuntimeInstanceId != request.RuntimeInstanceId)
                throw new ArgumentException("The workload belongs to another Runtime.", nameof(request));
        }
    }

    public async Task<RunnerCapacityReleaseOutcome> ReleaseAsync(
        Guid runtimeId, string runnerId, CancellationToken ct)
    {
        var runtime = await db.RuntimeInstances.AsNoTracking()
            .Include(item => item.CapacityAllocationEntries)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == runtimeId, ct);
        if (runtime is null) return RunnerCapacityReleaseOutcome.RecoveryRequired;
        var primary = runtime.CapacityAllocations.Items.SingleOrDefault(
            item => !item.Identity.IsAuxiliary);
        return primary is null ? RunnerCapacityReleaseOutcome.RecoveryRequired
            : await ReleaseWorkloadAsync(primary.Identity, runnerId, ct);
    }

    public async Task<RunnerCapacityReleaseOutcome> ReleaseWorkloadAsync(
        RuntimeWorkloadIdentity identity, string runnerId, CancellationToken ct)
    {
        var hasAmbientTransaction = db.Database.CurrentTransaction is not null;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await ReleaseWorkloadOnceAsync(identity, runnerId, ct);
            }
            catch (Exception exception) when (!hasAmbientTransaction && attempt < 2
                && IsTransactionFailure(exception))
            {
                outbox.DiscardPendingMessages();
                db.ChangeTracker.Clear();
                NoCtfTelemetry.RecordRunnerCapacityTransactionRetry(
                    RunnerCapacityTransactionOperation.Release);
                await Task.Delay(Random.Shared.Next(2, 18), ct);
            }
            catch (Exception exception) when (!hasAmbientTransaction
                && IsTransactionFailure(exception))
            {
                outbox.DiscardPendingMessages();
                NoCtfTelemetry.RecordRunnerCapacityTransactionExhaustion(
                    RunnerCapacityTransactionOperation.Release);
                throw;
            }
        }
    }

    private async Task<RunnerCapacityReleaseOutcome> ReleaseWorkloadOnceAsync(
        RuntimeWorkloadIdentity identity, string runnerId, CancellationToken ct)
    {
        using var ledgerLease = await ledgerCoordinator.EnterAsync(ct);
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var ledger = await db.RuntimeCapacityLedgers.SingleAsync(item => item.Id == 1, ct);
        var runtime = await db.RuntimeInstances
            .Include(item => item.CapacityAllocationEntries)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == identity.RuntimeInstanceId, ct);
        var allocation = runtime?.CapacityAllocations.Items.SingleOrDefault(
            item => item.Identity == identity);
        if (allocation is null) return RunnerCapacityReleaseOutcome.AlreadyReleased;
        if (allocation.RunnerId != runnerId)
            return RunnerCapacityReleaseOutcome.OwnerMismatch;
        runtime!.CapacityAllocationEntries.RemoveAll(entry =>
            entry.ToValue().Identity == identity);
        ledger.ConcurrencyStamp = Guid.NewGuid();
        await outbox.PublishAsync(new ReleaseRunnerCapacity(identity, runnerId));
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
            ledgerLease.Dispose();
            await outbox.FlushCommittedMessagesAsync();
        }
        return RunnerCapacityReleaseOutcome.Released;
    }

    private static bool IsTransactionFailure(Exception exception) =>
        TransactionFailureClassifier.IsRetryable(exception);

    public static RuntimeWorkloadIdentity PrimaryIdentity(RuntimeInstance runtime) => new(
        runtime.Purpose is RuntimePurpose.AwdpTarget or RuntimePurpose.PatchVerificationTarget
            ? RuntimeWorkloadKind.VerificationTarget
            : RuntimeWorkloadKind.Runtime,
        runtime.Id, runtime.Id);

    private static string WaitingKey(Guid runtimeId) =>
        $"runtime:waiting:{runtimeId:N}";
}
