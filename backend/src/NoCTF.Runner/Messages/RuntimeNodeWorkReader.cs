using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Runner.Messages;

public sealed class RuntimeNodeWorkReader(IServiceScopeFactory scopes) : IRuntimeNodeWorkReader
{
    public async Task<RuntimeProvisionWorkStatus> ReadProvisionStatusAsync(
        IRuntimeProvisionMessage message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        var assignment = await db.RuntimeInstances.AsNoTracking()
            .Where(candidate => candidate.Id == message.RuntimeInstanceId)
            .Select(candidate => new
            {
                candidate.State,
                candidate.RunnerId,
                candidate.ProviderReceiptJson
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (assignment is null
            || !string.Equals(assignment.RunnerId, message.RunnerId, StringComparison.Ordinal))
            return RuntimeProvisionWorkStatus.AssignmentAbsent;
        if (assignment.State == RuntimeState.Provisioning)
            return RuntimeProvisionWorkStatus.Current;
        if (assignment.State == RuntimeState.Stopping
            && assignment.ProviderReceiptJson == null)
            return RuntimeProvisionWorkStatus.StopRequested;
        return RuntimeProvisionWorkStatus.AssignmentRetained;
    }

    public async Task<RuntimeStopWork?> ReadStopAsync(
        IRuntimeStopMessage message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        return await db.RuntimeInstances.AsNoTracking()
            .Where(candidate => candidate.Id == message.RuntimeInstanceId
                && candidate.State == RuntimeState.Stopping
                && candidate.RunnerId == message.RunnerId)
            .Select(candidate => new RuntimeStopWork(
                candidate.RuntimeProvider,
                candidate.ProviderReceiptJson,
                candidate.RuntimeKind))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
