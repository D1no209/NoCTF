using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime;

public sealed record RuntimeHealthTarget(
    Guid InstanceId,
    string ResourceId,
    ContainerReceipt Receipt,
    RuntimeStatus PersistedStatus);

public interface IRuntimeHealthStore
{
    Task<IReadOnlyList<RuntimeHealthTarget>> ListActiveAsync(CancellationToken cancellationToken);
    Task<bool> UpdateStatusAsync(
        Guid instanceId,
        RuntimeStatus expectedStatus,
        RuntimeStatus status,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class ChallengeRuntimeHealthChecker(
    IRuntimeHealthStore store,
    IContainerLifecycle runtime)
{
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var changed = 0;
        foreach (var target in await store.ListActiveAsync(cancellationToken))
        {
            var current = await runtime.GetAsync(target.Receipt.Provider, target.ResourceId, cancellationToken);
            if (current is null)
            {
                if (await store.UpdateStatusAsync(target.InstanceId, target.PersistedStatus,
                        RuntimeStatus.Failed, DateTimeOffset.UtcNow, cancellationToken))
                    changed++;
                continue;
            }
            var observedStatus = current.Status == RuntimeStatus.Stopped
                ? RuntimeStatus.Failed
                : current.Status;
            if (observedStatus == target.PersistedStatus)
                continue;
            if (await store.UpdateStatusAsync(target.InstanceId, target.PersistedStatus,
                    observedStatus, DateTimeOffset.UtcNow, cancellationToken))
                changed++;
        }
        return changed;
    }
}
