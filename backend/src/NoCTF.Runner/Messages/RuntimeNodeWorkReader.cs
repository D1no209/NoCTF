using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Runner.Messages;

public sealed class RuntimeNodeWorkReader(IDbContextFactory<NoCtfDbContext> contexts)
    : IRuntimeNodeWorkReader
{
    public async Task<RuntimeProvisionWorkStatus> ReadProvisionStatusAsync(
        IRuntimeProvisionMessage message,
        CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var assignment = await db.RuntimeInstances.AsNoTracking()
            .AsSplitQuery()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == message.RuntimeInstanceId,
                cancellationToken);
        if (assignment is null
            || !string.Equals(assignment.RunnerId, message.RunnerId, StringComparison.Ordinal))
            return RuntimeProvisionWorkStatus.AssignmentAbsent;
        if (assignment.State == RuntimeState.Provisioning
            && assignment.CapacityAllocations.Items.Any(item => !item.Identity.IsAuxiliary
                && item.Identity.RuntimeInstanceId == message.RuntimeInstanceId
                && item.RunnerId == message.RunnerId && RuntimeProvisionCapacity.Matches(item, message)))
            return RuntimeProvisionWorkStatus.Current;
        if (assignment.State == RuntimeState.Stopping
            && assignment.ProviderReceipt == null)
            return RuntimeProvisionWorkStatus.StopRequested;
        return RuntimeProvisionWorkStatus.AssignmentRetained;
    }

    public async Task<RuntimeStopWork?> ReadStopAsync(
        IRuntimeStopMessage message,
        CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var candidate = await db.RuntimeInstances.AsNoTracking()
            .AsSplitQuery()
            .SingleOrDefaultAsync(candidate => candidate.Id == message.RuntimeInstanceId
                && candidate.State == RuntimeState.Stopping
                && candidate.RunnerId == message.RunnerId,
                cancellationToken);
        return candidate is null
            ? null
            : new RuntimeStopWork(
                candidate.RuntimeProvider,
                candidate.ProviderReceipt?.ToData(),
                candidate.RuntimeKind);
    }
}
