using AsyncKeyedLock;
using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Runner.Messages;

public static class IdempotentContainerProvisioner
{
    private static readonly AsyncKeyedLocker<Guid> Locks = new();

    public static async Task<ContainerReceipt> ProvisionAsync(
        IContainerLifecycle lifecycle,
        ContainerRequest request,
        CancellationToken cancellationToken)
    {
        using var releaser = await Locks.LockAsync(request.OperationId, cancellationToken);
        return await lifecycle.EnsureRunningAsync(request, cancellationToken);
    }
}
