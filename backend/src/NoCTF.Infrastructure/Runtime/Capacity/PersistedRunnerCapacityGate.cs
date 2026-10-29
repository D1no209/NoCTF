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
                && fact.State == GameplayFactState.Processing
                && (identity.Kind != RuntimeWorkloadKind.AwdChecker || fact.Awd != null
                    && fact.Awd.AttemptId == identity.OperationId && fact.Awd.RuntimeId == identity.RuntimeInstanceId
                    && fact.Awd.Execution == NoCTF.Domain.Competitions.Awd.AwdCheckExecution.Running), ct)
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
        for (var attempt = 0; attempt < RunnerCapacityTransactionRetry.MaximumAttempts; attempt++)
        {
            try { return await ClaimAsync(request, runnerId, ct); }
            catch (Exception exception) when (!hasAmbientTransaction && RunnerCapacityTransactionRetry.CanRetry(attempt)
                && IsTransactionFailure(exception))
            {
                NoCtfTelemetry.RecordRunnerCapacityTransactionRetry(
                    RunnerCapacityTransactionOperation.Claim);
                db.ChangeTracker.Clear();
                await RunnerCapacityTransactionRetry.DelayAsync(attempt, ct);
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
            // Selection is advisory. Each attempt revalidates the Runtime inside its
            // transaction. The global gate bounds local Serializable contention.
            var target = await db.RuntimeInstances.AsNoTracking().IgnoreAutoIncludes()
                .Where(item => item.Id == request.RuntimeInstanceId)
                .Select(item => new { item.RunnerId, item.ExecutionScopeId })
                .SingleOrDefaultAsync(ct);
            if (target is null)
                return new(RunnerCapacityAvailability.Unavailable,
                    Failure: RunnerAdmissionFailure.NoEligibleRunner);
            var candidates = eligible.Where(item => target.RunnerId is null
                    || item.RunnerId == target.RunnerId)
                .OrderBy(AdmissionPressure)
                .ThenBy(item => item.RunnerId, StringComparer.Ordinal)
                .Take(8).ToArray();
            if (candidates.Length == 0)
            {
                var result = await ClaimOnDomainAsync(request, requestedRunnerId, eligible, null, ct);
                availability = result.Availability;
                return result;
            }

            RunnerAdmissionFailure? firstFailure = null;
            foreach (var candidate in candidates)
            {
                attempts++;
                var result = await ClaimOnDomainAsync(request, requestedRunnerId, eligible, candidate, ct);
                if (result.Availability != RunnerCapacityAvailability.Insufficient)
                {
                    availability = result.Availability;
                    return result;
                }
                firstFailure ??= result.Failure;
            }
            availability = RunnerCapacityAvailability.Insufficient;
            return new(availability, Failure: firstFailure ?? RunnerAdmissionFailure.NoEligibleRunner);
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

    private async Task<RunnerCapacityClaim> ClaimOnDomainAsync(
        RunnerCapacityRequest request, string? requestedRunnerId,
        IReadOnlyList<RunnerAvailabilityRegistration> eligible,
        RunnerAvailabilityRegistration? candidate, CancellationToken ct)
    {
        var domain = candidate?.Admission.Observation?.ResourceDomain;
        using var ledgerLease = await ledgerCoordinator.EnterAsync(ct);
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        var current = await db.RuntimeInstances.AsNoTracking().IgnoreAutoIncludes()
            .Where(item => item.Id == request.RuntimeInstanceId)
            .Select(item => new
            {
                item.Id, item.ConcurrencyStamp, item.Purpose, item.State, item.RunnerId,
                item.ExecutionScopeId, item.CompetitionChallengeId, item.TeamId, item.GameplayFactId
            })
            .SingleOrDefaultAsync(ct);
        if (current is null
            || current.State is RuntimeState.Stopped or RuntimeState.Stopping or RuntimeState.Failed)
            return new(RunnerCapacityAvailability.Unavailable,
                Failure: RunnerAdmissionFailure.NoEligibleRunner);

        var identity = request.Workload ?? PrimaryIdentity(current.Purpose, current.Id);
        identity.Validate();
        if (identity.RuntimeInstanceId != current.Id)
            throw new InvalidOperationException("The capacity identity belongs to another Runtime.");
        if (!identity.IsAuxiliary && identity != PrimaryIdentity(current.Purpose, current.Id)
            || current.RunnerId is { } owner && requestedRunnerId is not null && owner != requestedRunnerId)
            return new(RunnerCapacityAvailability.Unavailable,
                Failure: RunnerAdmissionFailure.NoEligibleRunner);
        if (identity.IsAuxiliary && (request.GameplayFactId is not Guid factId
            || !await db.GameplayFacts.AnyAsync(fact => fact.Id == factId
                && fact.State == GameplayFactState.Processing
                && fact.CompetitionChallengeId == current.CompetitionChallengeId
                && fact.TeamId == current.TeamId
                && (identity.Kind == RuntimeWorkloadKind.AwdChecker
                    && fact.Kind == GameplayFactKind.AwdServiceTransition
                    && fact.Awd != null && fact.Awd.AttemptId == identity.OperationId
                    && fact.Awd.RuntimeId == current.Id
                    && fact.Awd.Execution == NoCTF.Domain.Competitions.Awd.AwdCheckExecution.Running
                    || identity.Kind == RuntimeWorkloadKind.PatchChecker
                        && current.GameplayFactId == fact.Id), ct)))
            return new(RunnerCapacityAvailability.Unavailable,
                Failure: RunnerAdmissionFailure.NoEligibleRunner);

        var existing = (await ReadAllocationAsync(identity, ct))?.ToValue();
        var limit = existing?.Limit ?? request.Limit
            ?? new RuntimeResourceAmount(request.MemoryBytes, request.CpuMillicores, request.PidsLimit);
        var budget = existing?.Budget
            ?? new RuntimeResourceAmount(request.MemoryBytes, request.CpuMillicores, request.PidsLimit);
        limit.Validate();
        budget.Validate();
        if (existing is not null)
        {
            if (requestedRunnerId is not null && existing.RunnerId != requestedRunnerId)
                return new(RunnerCapacityAvailability.Unavailable,
                    Failure: RunnerAdmissionFailure.NoEligibleRunner);
            var previousOwner = eligible.FirstOrDefault(item => item.RunnerId == existing.RunnerId
                && item.Admission.Observation?.ResourceDomain == existing.ResourceDomain);
            return previousOwner is null
                ? new(RunnerCapacityAvailability.Unavailable, Failure: RunnerAdmissionFailure.NoEligibleRunner)
                : new(RunnerCapacityAvailability.Claimed, existing.RunnerId, RunnerCapacityClaimState.AlreadyOwned);
        }
        if (candidate is null || domain is null
            || current.RunnerId is { } currentOwner && candidate.RunnerId != currentOwner)
            return new(RunnerCapacityAvailability.Insufficient,
                Failure: RunnerAdmissionFailure.NoEligibleRunner);

        var ledger = await ReadLedgerAsync(ct);
        var observedAt = candidate.Admission.Observation!.ObservedAt;
        // Count every owner in the real resource domain, including pending allocations
        // left by a previous Runner. Loading only a RunnerId can under-reserve at handoff.
        var reservations = await db.RuntimeInstances.AsNoTracking().IgnoreAutoIncludes()
            .SelectMany(item => item.CapacityAllocationEntries, (item, allocation) => new
            {
                item.State, item.RunningAt, Allocation = allocation
            })
            .Where(item => item.Allocation.ResourceDomain == domain
                && (item.Allocation.WorkloadKind != RuntimeWorkloadKind.AwdChecker
                    && item.Allocation.WorkloadKind != RuntimeWorkloadKind.PatchChecker
                    && (item.State != RuntimeState.Running || item.RunningAt == null || observedAt < item.RunningAt)
                    || (item.Allocation.WorkloadKind == RuntimeWorkloadKind.AwdChecker
                        || item.Allocation.WorkloadKind == RuntimeWorkloadKind.PatchChecker)
                        && item.Allocation.GameplayFactId != null
                        && db.GameplayFacts.Any(fact => fact.Id == item.Allocation.GameplayFactId
                            && fact.State == GameplayFactState.Processing)))
            .Select(item => new CapacityReservation(item.Allocation.WorkloadKind,
                item.Allocation.LimitMemoryBytes, item.Allocation.LimitCpuMillicores, item.Allocation.LimitPidsLimit,
                item.Allocation.WorkloadKind == RuntimeWorkloadKind.AwdChecker
                    || item.Allocation.WorkloadKind == RuntimeWorkloadKind.PatchChecker
                    || item.State == RuntimeState.Queued || item.State == RuntimeState.Provisioning
                    || item.State == RuntimeState.Running))
            .ToArrayAsync(ct);
        var failure = CheckCapacity(candidate, limit, reservations, identity.IsAuxiliary);
        if (failure is not null)
            return new(RunnerCapacityAvailability.Insufficient, Failure: failure);

        var entry = RuntimeCapacityAllocationEntry.FromValue(new(
            identity, request.GameplayFactId, domain, candidate.RunnerId, budget, limit));
        var runtime = await ReadRuntimeForWriteAsync(current.Id, current.ConcurrencyStamp, ct);
        runtime.CapacityAllocationEntries.Add(entry);
        db.Entry(entry).State = EntityState.Added;
        // The global row fences budget transactions across processes. The Runtime
        // version also prevents stale tracked allocation graphs from being reused.
        runtime.ConcurrencyStamp = Guid.NewGuid();
        ledger.ConcurrencyStamp = Guid.NewGuid();
        await SaveAllocationChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return new(RunnerCapacityAvailability.Claimed, candidate.RunnerId, RunnerCapacityClaimState.Acquired);
    }

    private Task<RuntimeCapacityAllocationEntry?> ReadAllocationAsync(
        RuntimeWorkloadIdentity identity, CancellationToken ct) =>
        db.RuntimeInstances.AsNoTracking().IgnoreAutoIncludes()
            .Where(item => item.Id == identity.RuntimeInstanceId)
            .SelectMany(item => item.CapacityAllocationEntries)
            .SingleOrDefaultAsync(item => item.WorkloadKind == identity.Kind
                && item.WorkloadRuntimeInstanceId == identity.RuntimeInstanceId
                && item.OperationId == identity.OperationId, ct);

    private async Task<RuntimeInstance> ReadRuntimeForWriteAsync(Guid id, Guid observedStamp, CancellationToken ct)
    {
        // Do not clear/reload an ambient caller's pending changes. Its original version
        // must still match the authoritative snapshot before using the tracked graph.
        var runtime = await db.RuntimeInstances.IgnoreAutoIncludes()
            .Include(item => item.ActiveSlot)
            .Include(item => item.CapacityAllocationEntries)
            .AsSplitQuery().SingleOrDefaultAsync(item => item.Id == id, ct)
            ?? throw new DbUpdateConcurrencyException("The capacity Runtime disappeared.");
        if (db.Entry(runtime).Property(item => item.ConcurrencyStamp).OriginalValue != observedStamp)
            throw new DbUpdateConcurrencyException("The tracked capacity Runtime no longer matches the current transaction.");
        return runtime;
    }

    private Task<RuntimeCapacityLedger> ReadLedgerAsync(CancellationToken ct) =>
        db.RuntimeCapacityLedgers.SingleAsync(item => item.Id == 1, ct);

    private async Task SaveAllocationChangesAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException exception) when (exception.GetBaseException() is System.Data.Common.DbException { SqlState: "23505" }
            && exception.Entries.Any(entry => entry.State == EntityState.Added
                && entry.Entity is RuntimeCapacityAllocationEntry))
        {
            // A duplicate workload identity may be inserted by another process.
            // Restart the complete transaction, including its reads.
            throw new DbUpdateConcurrencyException("Capacity allocation initialization conflicted with another transaction.", exception);
        }
    }

    private sealed record CapacityReservation(
        RuntimeWorkloadKind Kind, long MemoryBytes, long CpuMillicores, long PidsLimit,
        bool CountsTowardStartup)
    {
        public bool IsAuxiliary => Kind is RuntimeWorkloadKind.AwdChecker or RuntimeWorkloadKind.PatchChecker;
    }

    private static RunnerAdmissionFailure? CheckCapacity(
        RunnerAvailabilityRegistration candidate,
        RuntimeResourceAmount requested,
        IReadOnlyCollection<CapacityReservation> reservations,
        bool auxiliary)
    {
        var projection = candidate.Admission.Capacity!;
        if (requested.MemoryBytes > projection.ObservedTotal.MemoryBytes
            || requested.CpuMillicores > projection.ObservedTotal.CpuMillicores
            || projection.ObservedTotal.PidsLimit is long totalPids
                && requested.PidsLimit > totalPids)
            return RunnerAdmissionFailure.RequestExceedsNodeCapacity;
        // Cleanup keeps its physical resource budget until absence is confirmed,
        // but it must not hold a startup slot while another Runtime is being started.
        var starting = reservations.Count(item => item.IsAuxiliary == auxiliary && item.CountsTowardStartup);
        var maximum = auxiliary
            ? candidate.AdmissionOptions.AuxiliaryConcurrency
            : candidate.AdmissionOptions.MainStartupConcurrency;
        if (starting >= maximum)
            return RunnerAdmissionFailure.StartupConcurrencyLimited;
        var reservedMemory = reservations.Sum(item => item.MemoryBytes);
        var reservedCpu = reservations.Sum(item => item.CpuMillicores);
        var reservedPids = reservations.Sum(item => item.PidsLimit);
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
        return await ReleaseWorkloadAsync(
            primary?.Identity ?? PrimaryIdentity(runtime), runnerId, ct);
    }

    public async Task<RunnerCapacityReleaseOutcome> ReleaseWorkloadAsync(
        RuntimeWorkloadIdentity identity, string runnerId, CancellationToken ct)
    {
        var hasAmbientTransaction = db.Database.CurrentTransaction is not null;
        RunnerCapacityReleaseOutcome result;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                result = await ReleaseWorkloadOnceAsync(identity, runnerId, ct);
                break;
            }
            catch (Exception exception) when (!hasAmbientTransaction && RunnerCapacityTransactionRetry.CanRetry(attempt)
                && IsTransactionFailure(exception))
            {
                outbox.DiscardPendingMessages();
                db.ChangeTracker.Clear();
                NoCtfTelemetry.RecordRunnerCapacityTransactionRetry(
                    RunnerCapacityTransactionOperation.Release);
                await RunnerCapacityTransactionRetry.DelayAsync(attempt, ct);
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
        // Publication has an external outcome and must never enter the database retry
        // loop after a successful commit. Ambient callers own their publication boundary.
        if (!hasAmbientTransaction && result == RunnerCapacityReleaseOutcome.Released)
            await outbox.FlushCommittedMessagesAsync();
        return result;
    }

    private async Task<RunnerCapacityReleaseOutcome> ReleaseWorkloadOnceAsync(
        RuntimeWorkloadIdentity identity, string runnerId, CancellationToken ct)
    {
        var scope = await db.RuntimeInstances.AsNoTracking().IgnoreAutoIncludes()
            .Where(item => item.Id == identity.RuntimeInstanceId)
            .SelectMany(item => item.CapacityAllocationEntries)
            .Where(item => item.WorkloadKind == identity.Kind
                && item.WorkloadRuntimeInstanceId == identity.RuntimeInstanceId
                && item.OperationId == identity.OperationId)
            .Select(item => new { item.ResourceDomain, item.RunnerId })
            .SingleOrDefaultAsync(ct);
        if (scope is null) return RunnerCapacityReleaseOutcome.AlreadyReleased;
        if (scope.RunnerId != runnerId) return RunnerCapacityReleaseOutcome.OwnerMismatch;
        using var ledgerLease = await ledgerCoordinator.EnterAsync(ct);
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var stamp = await db.RuntimeInstances.AsNoTracking().IgnoreAutoIncludes()
            .Where(item => item.Id == identity.RuntimeInstanceId)
            .Select(item => (Guid?)item.ConcurrencyStamp).SingleOrDefaultAsync(ct);
        var allocation = await ReadAllocationAsync(identity, ct);
        if (allocation is null) return RunnerCapacityReleaseOutcome.AlreadyReleased;
        if (allocation.RunnerId != runnerId)
            return RunnerCapacityReleaseOutcome.OwnerMismatch;
        if (allocation.ResourceDomain != scope.ResourceDomain)
            return RunnerCapacityReleaseOutcome.RecoveryRequired;
        if (stamp is null) return RunnerCapacityReleaseOutcome.AlreadyReleased;
        var runtime = await ReadRuntimeForWriteAsync(identity.RuntimeInstanceId, stamp.Value, ct);
        var ledger = await ReadLedgerAsync(ct);
        var trackedAllocation = runtime.CapacityAllocationEntries.SingleOrDefault(entry => entry.Id == allocation.Id)
            ?? throw new DbUpdateConcurrencyException("The tracked capacity allocation no longer matches the current transaction.");
        runtime.CapacityAllocationEntries.Remove(trackedAllocation);
        runtime.ConcurrencyStamp = Guid.NewGuid();
        ledger.ConcurrencyStamp = Guid.NewGuid();
        await outbox.PublishAsync(new ReleaseRunnerCapacity(identity, runnerId));
        await SaveAllocationChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return RunnerCapacityReleaseOutcome.Released;
    }

    private static bool IsTransactionFailure(Exception exception) =>
        TransactionFailureClassifier.IsRetryable(exception);

    public static RuntimeWorkloadIdentity PrimaryIdentity(RuntimeInstance runtime) => PrimaryIdentity(runtime.Purpose, runtime.Id);

    private static RuntimeWorkloadIdentity PrimaryIdentity(RuntimePurpose purpose, Guid runtimeId) => new(
        purpose is RuntimePurpose.AwdpTarget or RuntimePurpose.PatchVerificationTarget
            ? RuntimeWorkloadKind.VerificationTarget
            : RuntimeWorkloadKind.Runtime,
        runtimeId, runtimeId);

    private static string WaitingKey(Guid runtimeId) =>
        $"runtime:waiting:{runtimeId:N}";
}
