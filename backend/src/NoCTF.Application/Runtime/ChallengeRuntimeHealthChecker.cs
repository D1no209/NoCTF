using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime;

public sealed record RuntimeHealthTarget(Guid InstanceId, string ResourceId, ContainerReceipt Receipt);

public interface IRuntimeHealthStore
{
    Task<IReadOnlyList<RuntimeHealthTarget>> ListActiveAsync(CancellationToken cancellationToken);
    Task UpdateStatusAsync(Guid instanceId, RuntimeStatus status, DateTimeOffset now, CancellationToken cancellationToken);
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
            if (current is null || current.Status == target.Receipt.Status)
                continue;
            await store.UpdateStatusAsync(target.InstanceId, current.Status, DateTimeOffset.UtcNow, cancellationToken);
            changed++;
        }
        return changed;
    }
}
