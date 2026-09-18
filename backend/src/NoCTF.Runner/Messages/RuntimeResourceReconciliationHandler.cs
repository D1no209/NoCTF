using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Composition;
using Wolverine.Attributes;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Application.Messaging;
using System.Text.Json;

namespace NoCTF.Runner.Messages;

// Provider cleanup and capacity release are external side effects; commit each successful marker clear independently.
[NonTransactional]
public sealed class RuntimeResourceReconciliationHandler(
    NoCtfDbContext db,
    IEnumerable<IRuntimeManagedResourceReconciler> reconcilers,
    IOptions<RunnerOptions> runnerOptions,
    IRunnerCapacityGate capacity,
    IRuntimeProviderCatalog? providers = null,
    NoCTF.Infrastructure.Runtime.Capacity.RunnerResourceMutationCoordinator? mutations = null,
    RedisRunnerCapacityLedger? ledger = null,
    RedisRunnerCapacityGate? rawCapacity = null,
    ITransactionalMessageOutbox? outbox = null,
    TimeProvider? clock = null)
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    public async Task Handle(
        ReconcileRuntimeResources message,
        CancellationToken cancellationToken)
    {
        using var mutation = mutations is null ? null : await mutations.EnterAsync(cancellationToken);
        var configuredPool = runnerOptions.Value.Pool;
        var configuredRunnerId = runnerOptions.Value.Id;
        RunnerNodeAssignmentGuard.Validate(
            message,
            configuredPool,
            configuredRunnerId);

        var configuredProvider = runnerOptions.Value.Provider
            ?? throw new InvalidOperationException("Runner:Provider is required.");
        var reconciler = reconcilers.SingleOrDefault(candidate =>
                candidate.Provider == configuredProvider)
            ?? throw new InvalidOperationException(
                $"Runtime resource reconciliation is unavailable for '{configuredProvider}'.");
        await ReconcileAuxiliaryAsync(reconciler, configuredRunnerId, cancellationToken);
        await ReconcileUnconfirmedAsync(reconciler, configuredRunnerId, cancellationToken);
        var managed = await reconciler.ListManagedAsync(cancellationToken);

        var failedAssignments = await db.RuntimeInstances
            .Where(instance => instance.State == RuntimeState.Failed
                && instance.RuntimeProvider == configuredProvider
                && instance.RunnerId == message.RunnerId)
            .OrderBy(instance => instance.Id)
            .ToListAsync(cancellationToken);
        var failedIdentities = failedAssignments
            .Select(instance => new RuntimeResourceIdentity(instance.Id))
            .ToHashSet();
        foreach (var instance in failedAssignments)
        {
            var identity = new RuntimeResourceIdentity(instance.Id);
            if (instance.ProviderReceiptJson is { } providerReceiptJson)
            {
                var catalog = providers
                    ?? throw new InvalidOperationException(
                        "Runtime provider catalog is required for receipt-based cleanup.");
                await RuntimeReceiptCleanup.CleanupAsync(
                    catalog,
                    instance.RuntimeKind,
                    identity,
                    instance.RuntimeProvider,
                    providerReceiptJson,
                    cancellationToken);
            }
            else
            {
                await reconciler.DestroyByIdentityAsync(identity, cancellationToken);
                var remaining = await reconciler.ListManagedAsync(cancellationToken);
                if (remaining.Contains(identity))
                    throw new InvalidOperationException(
                        "Failed Runtime resources remain after orphan cleanup.");
            }
            var release = await capacity.ReleaseAsync(
                instance.Id,
                message.RunnerId,
                cancellationToken);
            if (release is RunnerCapacityReleaseOutcome.OwnerMismatch or RunnerCapacityReleaseOutcome.RecoveryRequired)
                throw new InvalidOperationException(
                    "Failed Runtime capacity belongs to a different Runner assignment.");

            instance.ProviderReceiptJson = null;
            instance.RunnerId = null;
            await db.SaveChangesAsync(cancellationToken);
        }

        var runtimeIds = managed.Select(resource => resource.RuntimeInstanceId)
            .Distinct()
            .ToArray();
        IReadOnlyDictionary<Guid, RuntimeResourceAssignment> assignments =
            runtimeIds.Length == 0
            ? new Dictionary<Guid, RuntimeResourceAssignment>()
            : await db.RuntimeInstances.AsNoTracking()
                .Where(instance => runtimeIds.Contains(instance.Id))
                .Select(instance => new RuntimeResourceAssignment(
                    instance.Id,
                    instance.RuntimeProvider,
                    instance.State,
                    instance.RunnerId))
                .ToDictionaryAsync(instance => instance.Id, cancellationToken);

        var failures = 0;
        foreach (var resource in managed)
        {
            if (failedIdentities.Contains(resource))
                continue;
            assignments.TryGetValue(resource.RuntimeInstanceId, out var assignment);
            var matchesAssignment = assignment is not null
                && assignment.RuntimeProvider == configuredProvider
                && string.Equals(
                    assignment.RunnerId,
                    message.RunnerId,
                    StringComparison.Ordinal);
            var hasActiveAssignment = matchesAssignment
                && assignment!.State is RuntimeState.Provisioning
                    or RuntimeState.Running
                    or RuntimeState.Stopping;
            if (hasActiveAssignment)
                continue;
            try
            {
                await reconciler.DestroyByIdentityAsync(resource, cancellationToken);
                if (CanReleaseOrphanCapacity(
                        assignment,
                        configuredProvider,
                        message.RunnerId))
                {
                    var release = await capacity.ReleaseAsync(
                        resource.RuntimeInstanceId,
                        message.RunnerId,
                        cancellationToken);
                    if (release is RunnerCapacityReleaseOutcome.OwnerMismatch or RunnerCapacityReleaseOutcome.RecoveryRequired)
                        throw new InvalidOperationException(
                            "Orphaned Runtime capacity belongs to a different Runner assignment.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                failures++;
            }
        }

        if (failures > 0)
            throw new InvalidOperationException(
                $"{failures} orphaned Runtime resource groups could not be removed.");
    }

    private async Task ReconcileUnconfirmedAsync(IRuntimeManagedResourceReconciler reconciler, string runnerId, CancellationToken ct)
    {
        if (ledger is null || rawCapacity is null || !db.Database.IsRelational()) return;
        foreach (var identity in await ledger.ReadUnconfirmedAsync(runnerId, ct))
        {
            var document = await db.RuntimeInstances.AsNoTracking().Where(row => row.Id == identity.RuntimeInstanceId)
                .Select(row => row.CapacityAllocations).SingleOrDefaultAsync(ct);
            if (document?.Items.Any(item => item.Identity == identity && item.RunnerId == runnerId) == true)
            {
                await ledger.ConfirmAsync(runnerId, identity, ct);
                continue;
            }
            await ledger.DeferConfirmationAsync(runnerId, identity, ct);
            // No database lock crosses the provider call; unknown resources retain their claim.
            if (await reconciler.WorkloadExistsAsync(identity, ct) != false) continue;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await RuntimeCapacityCriticalSection.AcquireAsync(db, ct);
            document = await db.RuntimeInstances.AsNoTracking().Where(row => row.Id == identity.RuntimeInstanceId)
                .Select(row => row.CapacityAllocations).SingleOrDefaultAsync(ct);
            if (document?.Items.Any(item => item.Identity == identity) == true) continue;
            var released = await rawCapacity.ReleaseWorkloadAsync(identity, runnerId, ct);
            if (released == RunnerCapacityReleaseOutcome.AlreadyReleased) await ledger.ConfirmAsync(runnerId, identity, ct);
            if (released == RunnerCapacityReleaseOutcome.Released && outbox is not null)
                await outbox.PublishAsync(new DispatchQueuedRuntimes(timeProvider.GetUtcNow()));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            if (outbox is not null) await outbox.FlushCommittedMessagesAsync();
        }
    }

    private async Task ReconcileAuxiliaryAsync(IRuntimeManagedResourceReconciler reconciler, string runnerId, CancellationToken ct)
    {
        if (providers is null || !db.Database.IsRelational()) return;
        var awdOwner = JsonSerializer.Serialize(new { items = new[] { new { runnerId, identity = new { kind = RuntimeWorkloadKind.AwdChecker } } } });
        var patchOwner = JsonSerializer.Serialize(new { items = new[] { new { runnerId, identity = new { kind = RuntimeWorkloadKind.PatchChecker } } } });
        var rows = await db.RuntimeInstances.FromSqlInterpolated($"""
            SELECT * FROM runtime_instances
            WHERE runtime_provider = {(short)reconciler.Provider}
              AND (capacity_allocations @> CAST({awdOwner} AS jsonb)
                OR capacity_allocations @> CAST({patchOwner} AS jsonb))
            ORDER BY id LIMIT 500
            """).AsNoTracking().ToArrayAsync(ct);
        foreach (var runtime in rows.Where(row => row.RuntimeProvider == reconciler.Provider))
        foreach (var allocation in runtime.CapacityAllocations.Items.Where(item => item.Identity.IsAuxiliary && item.RunnerId == runnerId))
        {
            if (mutations?.IsActive(allocation.Identity) == true) continue;
            var processing = await db.GameplayFacts.AnyAsync(fact => fact.Id == allocation.GameplayFactId
                && fact.State == NoCTF.Domain.Gameplay.GameplayFactState.Processing, ct);
            if (processing && runtime.State is RuntimeState.Running or RuntimeState.Provisioning) continue;
            if (await reconciler.WorkloadExistsAsync(allocation.Identity, ct) == true)
            {
                var receipt = new ContainerReceipt(allocation.Identity.OperationId, runtime.RuntimeProvider,
                    $"noctf-{allocation.Identity.OperationId:N}", RuntimeStatus.Stopped,
                    new Dictionary<int, int>(), null, null, RuntimeInstanceId: runtime.Id);
                await providers.Containers(runtime.RuntimeProvider).DestroyAsync(receipt, ct);
            }
            if (await reconciler.WorkloadExistsAsync(allocation.Identity, ct) == false)
                await capacity.ReleaseWorkloadAsync(allocation.Identity, runnerId, ct);
        }
    }

    private static bool CanReleaseOrphanCapacity(
        RuntimeResourceAssignment? assignment,
        RuntimeProvider configuredProvider,
        string runnerId)
    {
        if (assignment is null)
            return true;
        if (assignment.State is RuntimeState.Provisioning
            or RuntimeState.Running
            or RuntimeState.Stopping)
            return false;
        return assignment.RuntimeProvider == configuredProvider
            && (assignment.RunnerId is null
                || string.Equals(assignment.RunnerId, runnerId, StringComparison.Ordinal));
    }

    private sealed record RuntimeResourceAssignment(
        Guid Id,
        RuntimeProvider RuntimeProvider,
        RuntimeState State,
        string? RunnerId);
}
