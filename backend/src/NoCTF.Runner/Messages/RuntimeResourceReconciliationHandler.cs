using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Composition;
using Wolverine.Attributes;

namespace NoCTF.Runner.Messages;

// Provider cleanup and capacity release are external side effects; commit each successful marker clear independently.
[NonTransactional]
public sealed class RuntimeResourceReconciliationHandler(
    NoCtfDbContext db,
    IEnumerable<IRuntimeManagedResourceReconciler> reconcilers,
    IOptions<RunnerOptions> runnerOptions,
    IRunnerCapacityGate capacity,
    IRuntimeProviderCatalog? providers = null)
{
    public async Task Handle(
        ReconcileRuntimeResources message,
        CancellationToken cancellationToken)
    {
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
            if (release == RunnerCapacityReleaseOutcome.OwnerMismatch)
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
                    if (release == RunnerCapacityReleaseOutcome.OwnerMismatch)
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
