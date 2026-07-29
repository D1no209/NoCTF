using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Composition;

namespace NoCTF.Runner.Messages;

public sealed class RuntimeResourceReconciliationHandler(
    NoCtfDbContext db,
    IEnumerable<IRuntimeManagedResourceReconciler> reconcilers,
    IConfiguration configuration)
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
            var isOwned = assignments.TryGetValue(resource.RuntimeInstanceId, out var assignment)
                && assignment.Generation == resource.Generation
                && assignment.RuntimeProvider == configuredProvider
                && assignment.State is RuntimeState.Provisioning
                    or RuntimeState.Running
                    or RuntimeState.Stopping
                && string.Equals(
                    assignment.RunnerPool,
                    message.RunnerPool,
                    StringComparison.Ordinal)
                && string.Equals(
                    assignment.RunnerId,
                    message.RunnerId,
                    StringComparison.Ordinal);
            if (isOwned)
                continue;
            try
            {
                await reconciler.DestroyByIdentityAsync(resource, cancellationToken);
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

    private sealed record RuntimeResourceAssignment(
        Guid Id,
        int Generation,
        RuntimeProvider RuntimeProvider,
        RuntimeState State,
        string RunnerPool,
        string? RunnerId);
}
