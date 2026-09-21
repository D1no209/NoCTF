using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Runner.Messages;

public static class IsolatedContainerProvisioner
{
    private static readonly TimeSpan ProvisioningCleanupBudget = TimeSpan.FromSeconds(15);

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
            new ContainerNetworkPolicyRequest(
                new RuntimeResourceIdentity(request.RuntimeInstanceId ?? request.OperationId),
                request.NetworkPurpose,
                request.EgressPolicy,
                request.PortMappings.Keys.Order().ToArray(),
                request.NetworkPurpose == ContainerNetworkPurpose.AwdpVerification
                    ? request.InternalPorts?.SingleOrDefault()
                    : null,
                request.AccessMode is NoCTF.Domain.Runtime.RuntimeAccessMode.DirectAndWsrx
                    or NoCTF.Domain.Runtime.RuntimeAccessMode.WsrxOnly
                    ? (request.UrlBindings ?? [])
                        .Select(binding => binding.ContainerPort)
                        .OfType<int>()
                        .Distinct()
                        .Order()
                        .ToArray()
                    : []),
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
            using var cleanup = new CancellationTokenSource(ProvisioningCleanupBudget);
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
        await DestroyAsync(
            lifecycle,
            sandbox,
            receipt,
            RuntimeTerminationMode.GracefulThenForce,
            RuntimeTerminationPolicy.Default,
            cancellationToken);
    }

    public static async Task DestroyAsync(
        IContainerLifecycle lifecycle,
        IContainerSandboxLifecycle sandbox,
        ContainerReceipt receipt,
        RuntimeTerminationMode mode,
        RuntimeTerminationPolicy policy,
        CancellationToken cancellationToken)
    {
        List<Exception>? warnings = null;
        try
        {
            await lifecycle.DestroyAsync(receipt, mode, policy, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            (warnings ??= []).Add(exception);
        }

        if (receipt.NetworkId is { Length: > 0 } networkId)
        {
            using var network = CreateStageToken(
                cancellationToken,
                policy.NetworkCleanupTimeout);
            try
            {
                await sandbox.DeleteIsolatedNetworkAsync(networkId, network.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                (warnings ??= []).Add(exception);
            }
        }

        using var verification = CreateStageToken(
            cancellationToken,
            policy.VerificationTimeout);
        try
        {
            await WaitUntilAbsentAsync(lifecycle, sandbox, receipt, verification.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "Runtime container or isolated network remains after cleanup.",
                warnings is null ? null : new AggregateException(warnings));
        }
    }

    private static async Task WaitUntilAbsentAsync(
        IContainerLifecycle lifecycle,
        IContainerSandboxLifecycle sandbox,
        ContainerReceipt receipt,
        CancellationToken cancellationToken)
    {
        var delays = new[] { 200, 400, 800, 1_000 };
        var attempt = 0;
        while (true)
        {
            var container = await lifecycle.GetAsync(
                receipt.Provider,
                receipt.ResourceId,
                cancellationToken);
            var networkExists = receipt.NetworkId is { Length: > 0 } networkId
                && await sandbox.IsolatedNetworkExistsAsync(networkId, cancellationToken);
            if (container is null && !networkExists)
                return;
            await Task.Delay(
                TimeSpan.FromMilliseconds(delays[Math.Min(attempt++, delays.Length - 1)]),
                cancellationToken);
        }
    }

    private static CancellationTokenSource CreateStageToken(
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }
}
