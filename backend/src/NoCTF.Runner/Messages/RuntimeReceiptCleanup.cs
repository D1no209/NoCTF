using System.Text.Json;
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
        string providerReceiptJson,
        CancellationToken cancellationToken) => runtimeKind switch
        {
            RuntimeKind.Container => CleanupContainerAsync(
                providers, identity, provider, providerReceiptJson, cancellationToken),
            RuntimeKind.Compose => CleanupComposeAsync(
                providers, identity, provider, providerReceiptJson, cancellationToken),
            RuntimeKind.OvaVm => CleanupOvaAsync(
                providers, identity, provider, providerReceiptJson, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(runtimeKind), runtimeKind, null)
        };

    public static async Task CleanupContainerAsync(
        IRuntimeProviderCatalog providers,
        RuntimeResourceIdentity identity,
        RuntimeProvider provider,
        string providerReceiptJson,
        CancellationToken cancellationToken)
    {
        var receipt = Deserialize<ContainerReceipt>(providerReceiptJson);
        if (receipt.Provider != provider
            || receipt.RuntimeInstanceId != identity.RuntimeInstanceId
            || receipt.Generation != identity.Generation
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
            cancellationToken);

        if (await lifecycle.GetAsync(provider, receipt.ResourceId, cancellationToken) is not null)
            throw new InvalidOperationException(
                "Container remains after receipt-based cleanup.");
        if (receipt.NetworkId is { Length: > 0 } networkId
            && await sandbox.IsolatedNetworkExistsAsync(networkId, cancellationToken))
        {
            throw new InvalidOperationException(
                "Container network remains after receipt-based cleanup.");
        }
    }

    public static async Task CleanupComposeAsync(
        IRuntimeProviderCatalog providers,
        RuntimeResourceIdentity identity,
        RuntimeProvider provider,
        string providerReceiptJson,
        CancellationToken cancellationToken)
    {
        var receipt = Deserialize<ComposeReceipt>(providerReceiptJson);
        if (receipt.Provider != provider
            || receipt.OperationId != identity.RuntimeInstanceId
            || receipt.Generation != identity.Generation
            || string.IsNullOrWhiteSpace(receipt.ProjectName)
            || string.IsNullOrWhiteSpace(receipt.Namespace))
        {
            throw new InvalidOperationException(
                "Compose receipt does not match the Runtime assignment.");
        }

        var runtime = providers.Compose(provider);
        await runtime.DownAsync(receipt, cancellationToken);
        var status = await runtime.GetStatusAsync(receipt, cancellationToken);
        if (status is not null
            && (status.Status != RuntimeStatus.Stopped || status.Services.Count > 0))
        {
            throw new InvalidOperationException(
                "Compose resources remain after receipt-based cleanup.");
        }
    }

    public static async Task CleanupOvaAsync(
        IRuntimeProviderCatalog providers,
        RuntimeResourceIdentity identity,
        RuntimeProvider provider,
        string providerReceiptJson,
        CancellationToken cancellationToken)
    {
        var receipt = Deserialize<OvaRuntimeReceipt>(providerReceiptJson);
        if (provider != RuntimeProvider.Libvirt
            || receipt.Provider != provider
            || receipt.OperationId != identity.RuntimeInstanceId
            || receipt.Generation != identity.Generation)
        {
            throw new InvalidOperationException(
                "OVA receipt does not match the Runtime assignment.");
        }

        var runtime = providers.Appliance(provider);
        await runtime.DestroyAsync(receipt, cancellationToken);
        var remaining = await runtime.ListManagedAsync(cancellationToken);
        if (remaining.Any(candidate =>
                candidate.OperationId == identity.RuntimeInstanceId
                && candidate.Generation == identity.Generation))
        {
            throw new InvalidOperationException(
                "OVA resources remain after receipt-based cleanup.");
        }
    }

    private static TReceipt Deserialize<TReceipt>(string providerReceiptJson)
    {
        try
        {
            return JsonSerializer.Deserialize<TReceipt>(providerReceiptJson)
                ?? throw new InvalidOperationException("Provider receipt is invalid.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Provider receipt is invalid.", exception);
        }
    }
}
