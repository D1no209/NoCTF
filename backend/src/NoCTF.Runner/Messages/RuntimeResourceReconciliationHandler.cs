using Microsoft.EntityFrameworkCore;
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
    IConfiguration configuration,
    IRunnerCapacityGate capacity)
{
    public async Task Handle(
        ReconcileRuntimeResources message,
        CancellationToken cancellationToken)
    {
        var configuredPool = configuration["Runner:Pool"] ?? "default";
        var configuredRunnerId = configuration["Runner:Id"]
            ?? throw new InvalidOperationException("Runner:Id is required.");
        RunnerNodeAssignmentGuard.Validate(
            message,
            configuredPool,
            configuredRunnerId);

        if (!Enum.TryParse<RuntimeProvider>(
                configuration["Runner:Provider"],
                ignoreCase: true,
                out var configuredProvider)
            || !Enum.IsDefined(configuredProvider))
            throw new InvalidOperationException("Runner:Provider is required.");
        var reconciler = reconcilers.SingleOrDefault(candidate =>
                candidate.Provider == configuredProvider)
            ?? throw new InvalidOperationException(
                $"Runtime resource reconciliation is unavailable for '{configuredProvider}'.");
        var managed = await reconciler.ListManagedAsync(cancellationToken);

        var failedAssignments = await db.RuntimeInstances
            .Where(instance => instance.State == RuntimeState.Failed
                && (instance.ProviderReceiptJson != null
                    || instance.FailureCode == RuntimeFailureCode.CleanupFailed)
                && instance.RuntimeProvider == configuredProvider
                && instance.RunnerPool == message.RunnerPool
                && instance.RunnerId == message.RunnerId)
            .OrderBy(instance => instance.Id)
            .ToListAsync(cancellationToken);
        var failedIdentities = failedAssignments
            .Select(instance => new RuntimeResourceIdentity(instance.Id, instance.Generation))
            .ToHashSet();
        foreach (var instance in failedAssignments)
        {
            await reconciler.DestroyByIdentityAsync(
                new RuntimeResourceIdentity(instance.Id, instance.Generation),
                cancellationToken);
            var release = await capacity.ReleaseAsync(
                instance.Id,
                message.RunnerId,
                cancellationToken);
            if (release == RunnerCapacityReleaseOutcome.OwnerMismatch)
                throw new InvalidOperationException(
                    "Failed Runtime capacity belongs to a different Runner assignment.");

            instance.ProviderReceiptJson = null;
            instance.RunnerId = null;
            instance.RunnerAssignmentReleaseToken = null;
            instance.RunnerUnavailableAt = null;
            instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
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
                    instance.Generation,
                    instance.RuntimeProvider,
                    instance.State,
                    instance.RunnerPool,
                    instance.RunnerId))
                .ToDictionaryAsync(instance => instance.Id, cancellationToken);

        var failures = 0;
        foreach (var resource in managed)
        {
            if (failedIdentities.Contains(resource))
                continue;
            assignments.TryGetValue(resource.RuntimeInstanceId, out var assignment);
            var matchesAssignment = assignment is not null
                && assignment.Generation == resource.Generation
                && assignment.RuntimeProvider == configuredProvider;
            var hasActiveAssignment = matchesAssignment
                && assignment!.State is RuntimeState.Provisioning
                    or RuntimeState.Running
                    or RuntimeState.Stopping;
            var belongsToAnotherRunner = matchesAssignment
                && assignment!.RunnerId is not null
                && !string.Equals(
                    assignment.RunnerId,
                    message.RunnerId,
                    StringComparison.Ordinal);
            if (hasActiveAssignment || belongsToAnotherRunner)
                continue;
            try
            {
                await reconciler.DestroyByIdentityAsync(resource, cancellationToken);
                if (CanReleaseOrphanCapacity(
                        assignment,
                        resource,
                        configuredProvider,
                        message.RunnerPool,
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
        RuntimeResourceIdentity resource,
        RuntimeProvider configuredProvider,
        string runnerPool,
        string runnerId)
    {
        if (assignment is null)
            return true;
        if (assignment.State is RuntimeState.Provisioning
            or RuntimeState.Running
            or RuntimeState.Stopping)
            return false;
        return assignment.RuntimeProvider == configuredProvider
            && assignment.Generation == resource.Generation
            && string.Equals(assignment.RunnerPool, runnerPool, StringComparison.Ordinal)
            && (assignment.RunnerId is null
                || string.Equals(assignment.RunnerId, runnerId, StringComparison.Ordinal));
    }

    private sealed record RuntimeResourceAssignment(
        Guid Id,
        int Generation,
        RuntimeProvider RuntimeProvider,
        RuntimeState State,
        string RunnerPool,
        string? RunnerId);
}
