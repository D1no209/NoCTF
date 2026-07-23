using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Runner.Messages;

public static class IsolatedContainerProvisioner
{
    private static readonly TimeSpan CleanupBudget = TimeSpan.FromSeconds(15);

    public static async Task<ContainerReceipt> ProvisionAsync(
        IContainerLifecycle lifecycle,
        IContainerSandboxLifecycle sandbox,
        ContainerRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (request.NetworkIsolation != ContainerNetworkIsolation.Isolated)
            return await IdempotentContainerProvisioner.ProvisionAsync(
                lifecycle,
                request,
                cancellationToken);

        var networkId = await sandbox.CreateIsolatedNetworkAsync(
            new RuntimeResourceIdentity(request.OperationId, request.Generation),
            now.Add(request.Ttl ?? TimeSpan.FromMinutes(15)),
            cancellationToken);
        try
        {
            var receipt = await IdempotentContainerProvisioner.ProvisionAsync(
                lifecycle,
                request with { NetworkName = networkId },
                cancellationToken);
            return receipt with { NetworkId = networkId };
        }
        catch
        {
            using var cleanup = new CancellationTokenSource(CleanupBudget);
            await sandbox.DeleteIsolatedNetworkAsync(networkId, cleanup.Token);
            throw;
        }
    }

    public static async Task DestroyAsync(
        IContainerLifecycle lifecycle,
        IContainerSandboxLifecycle sandbox,
        ContainerReceipt receipt,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        List<Exception>? failures = null;
        using var cleanup = new CancellationTokenSource(CleanupBudget);
        try
        {
            await lifecycle.DestroyAsync(receipt, cleanup.Token);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        if (receipt.NetworkId is { Length: > 0 } networkId)
        {
            try
            {
                await sandbox.DeleteIsolatedNetworkAsync(networkId, cleanup.Token);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is [var failure])
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        if (failures is { Count: > 1 })
            throw new AggregateException("Runtime cleanup failed.", failures);
    }
}
