using NoCTF.Application.Runtime.Provisioning;
using System.Collections.Concurrent;

namespace NoCTF.Runner.Messages;

public static class IdempotentContainerProvisioner
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    public static async Task<ContainerReceipt> ProvisionAsync(
        IContainerLifecycle lifecycle,
        ContainerRequest request,
        CancellationToken cancellationToken)
    {
        var gate = Locks.GetOrAdd(request.OperationId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await lifecycle.EnsureRunningAsync(request, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }
}
