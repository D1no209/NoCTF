using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Composition;

namespace NoCTF.Runner.Messages;

public sealed class LibvirtResourceReconciliationHandler(
    NoCtfDbContext db,
    IRuntimeProviderCatalog providers,
    IConfiguration configuration)
{
    public async Task Handle(
        ReconcileLibvirtResources message,
        CancellationToken cancellationToken)
    {
        var configuredPool = configuration["Runner:Pool"] ?? "default";
        var configuredRunnerId = configuration["Runner:Id"]
            ?? throw new InvalidOperationException("Runner:Id is required.");
        RunnerNodeAssignmentGuard.Validate(
            message,
            configuredPool,
            configuredRunnerId);

        var runtime = providers.Appliance(RuntimeProvider.Libvirt);
        var managed = await runtime.ListManagedAsync(cancellationToken);
        if (managed.Count == 0)
            return;

        var runtimeIds = managed.Select(resource => resource.OperationId)
            .Distinct()
            .ToArray();
        var assignments = await db.RuntimeInstances.AsNoTracking()
            .Where(instance => runtimeIds.Contains(instance.Id))
            .Select(instance => new
            {
                instance.Id,
                instance.Generation,
                instance.RuntimeProvider,
                instance.State,
                instance.RunnerPool,
                instance.RunnerId
            })
            .ToDictionaryAsync(instance => instance.Id, cancellationToken);

        var failures = 0;
        foreach (var resource in managed)
        {
            var isOwned = assignments.TryGetValue(resource.OperationId, out var assignment)
                && assignment.Generation == resource.Generation
                && assignment.RuntimeProvider == RuntimeProvider.Libvirt
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
                await runtime.DestroyByIdentityAsync(resource, cancellationToken);
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
                $"{failures} orphaned Libvirt Runtime resource groups could not be removed.");
    }
}
