using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;

namespace NoCTF.Runner.Messages;

public static class RuntimeReceiptCleanup
{
    public static Task CleanupAsync(
        IRuntimeProviderCatalog providers,
        RuntimeKind runtimeKind,
        RuntimeResourceIdentity identity,
        RuntimeProvider provider,
        RuntimeReceiptData providerReceipt,
        CancellationToken cancellationToken) => CleanupAsync(
            providers,
            runtimeKind,
            identity,
            provider,
            providerReceipt,
            RuntimeTerminationMode.GracefulThenForce,
            RuntimeTerminationPolicy.Default,
            cancellationToken);

    public static Task CleanupAsync(
        IRuntimeProviderCatalog providers,
        RuntimeKind runtimeKind,
        RuntimeResourceIdentity identity,
        RuntimeProvider provider,
        RuntimeReceiptData providerReceipt,
        RuntimeTerminationMode mode,
        RuntimeTerminationPolicy policy,
        CancellationToken cancellationToken) => runtimeKind switch
        {
            RuntimeKind.Container => CleanupContainerAsync(
                providers, identity, provider, providerReceipt, mode, policy, cancellationToken),
            RuntimeKind.Compose => CleanupComposeAsync(
                providers, identity, provider, providerReceipt, mode, policy, cancellationToken),
            RuntimeKind.OvaVm => CleanupOvaAsync(
                providers, identity, provider, providerReceipt, mode, policy, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(runtimeKind), runtimeKind, null)
        };

    public static async Task CleanupContainerAsync(
        IRuntimeProviderCatalog providers,
        RuntimeResourceIdentity identity,
        RuntimeProvider provider,
        RuntimeReceiptData providerReceipt,
        CancellationToken cancellationToken)
    {
        await CleanupContainerAsync(
            providers,
            identity,
            provider,
            providerReceipt,
            RuntimeTerminationMode.GracefulThenForce,
            RuntimeTerminationPolicy.Default,
            cancellationToken);
    }

    public static async Task CleanupContainerAsync(
        IRuntimeProviderCatalog providers,
        RuntimeResourceIdentity identity,
        RuntimeProvider provider,
        RuntimeReceiptData providerReceipt,
        RuntimeTerminationMode mode,
        RuntimeTerminationPolicy policy,
        CancellationToken cancellationToken)
    {
        var receipt = (providerReceipt as ContainerRuntimeReceiptData)?.ToReceipt()
            ?? throw new InvalidOperationException("Container receipt type is invalid.");
        if (receipt.Provider != provider
            || receipt.RuntimeInstanceId != identity.RuntimeInstanceId
            || string.IsNullOrWhiteSpace(receipt.ResourceId))
        {
            throw new InvalidOperationException(
                "Container receipt does not match the Runtime assignment.");
        }

        var lifecycle = providers.Containers(provider);
        var sandbox = providers.Sandbox(provider);
        await IsolatedContainerProvisioner.DestroyAsync(
            lifecycle,
            sandbox,
            receipt,
            mode,
            policy,
            cancellationToken);
    }

    public static async Task CleanupComposeAsync(
        IRuntimeProviderCatalog providers,
        RuntimeResourceIdentity identity,
        RuntimeProvider provider,
        RuntimeReceiptData providerReceipt,
        CancellationToken cancellationToken)
    {
        await CleanupComposeAsync(
            providers,
            identity,
            provider,
            providerReceipt,
            RuntimeTerminationMode.GracefulThenForce,
            RuntimeTerminationPolicy.Default,
            cancellationToken);
    }

    public static async Task CleanupComposeAsync(
        IRuntimeProviderCatalog providers,
        RuntimeResourceIdentity identity,
        RuntimeProvider provider,
        RuntimeReceiptData providerReceipt,
        RuntimeTerminationMode mode,
        RuntimeTerminationPolicy policy,
        CancellationToken cancellationToken)
    {
        var receipt = (providerReceipt as ComposeRuntimeReceiptData)?.ToReceipt()
            ?? throw new InvalidOperationException("Compose receipt type is invalid.");
        if (receipt.Provider != provider
            || receipt.OperationId != identity.RuntimeInstanceId
            || string.IsNullOrWhiteSpace(receipt.ProjectName)
            || string.IsNullOrWhiteSpace(receipt.Namespace))
        {
            throw new InvalidOperationException(
                "Compose receipt does not match the Runtime assignment.");
        }

        var runtime = providers.Compose(provider);
        await runtime.DownAsync(receipt, mode, policy, cancellationToken);
    }

    public static async Task CleanupOvaAsync(
        IRuntimeProviderCatalog providers,
        RuntimeResourceIdentity identity,
        RuntimeProvider provider,
        RuntimeReceiptData providerReceipt,
        CancellationToken cancellationToken)
    {
        await CleanupOvaAsync(
            providers,
            identity,
            provider,
            providerReceipt,
            RuntimeTerminationMode.GracefulThenForce,
            RuntimeTerminationPolicy.Default,
            cancellationToken);
    }

    public static async Task CleanupOvaAsync(
        IRuntimeProviderCatalog providers,
        RuntimeResourceIdentity identity,
        RuntimeProvider provider,
        RuntimeReceiptData providerReceipt,
        RuntimeTerminationMode mode,
        RuntimeTerminationPolicy policy,
        CancellationToken cancellationToken)
    {
        var receipt = (providerReceipt as OvaRuntimeReceiptData)?.ToReceipt()
            ?? throw new InvalidOperationException("OVA receipt type is invalid.");
        if (provider != RuntimeProvider.Libvirt
            || receipt.Provider != provider
            || receipt.OperationId != identity.RuntimeInstanceId)
        {
            throw new InvalidOperationException(
                "OVA receipt does not match the Runtime assignment.");
        }

        var runtime = providers.Appliance(provider);
        await runtime.DestroyAsync(receipt, mode, policy, cancellationToken);
    }
}
