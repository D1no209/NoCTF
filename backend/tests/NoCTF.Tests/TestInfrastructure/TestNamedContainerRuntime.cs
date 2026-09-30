using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Domain.Runtime;

internal sealed class TestNamedContainerRuntime(IContainerLifecycle containers, IContainerSandboxLifecycle sandbox) : IContainerRuntime
{
    public async Task<ContainerDeploymentReceipt> UpAsync(ContainerRuntimeRequest request, CancellationToken cancellationToken)
    {
        var service = request.Services.Single();
        var receipt = await containers.EnsureRunningAsync(new(request.OperationId, request.Provider, service.Image,
            service.Command ?? [], service.Environment ?? new Dictionary<string, string>(), request.Labels,
            (request.UrlBindings ?? []).Select(binding => binding.ContainerPort!.Value).Distinct().ToDictionary(port => port, _ => 0),
            request.Limits, request.Ttl, RuntimeInstanceId: request.OperationId, UrlBindings: request.UrlBindings), cancellationToken);
        return RuntimeReceiptTestData.Deployment(receipt);
    }
    public Task DownAsync(ContainerDeploymentReceipt receipt, CancellationToken cancellationToken) => DownAsync(receipt, RuntimeTerminationMode.GracefulThenForce, RuntimeTerminationPolicy.Default, cancellationToken);
    public async Task DownAsync(ContainerDeploymentReceipt receipt, RuntimeTerminationMode mode, RuntimeTerminationPolicy policy, CancellationToken cancellationToken)
    {
        foreach (var service in receipt.Services)
        {
            var target = NamedContainerRuntime.ServiceReceipt(receipt, service) with { NetworkId = receipt.OwnedNetworkId };
            await containers.DestroyAsync(target, mode, policy, cancellationToken);
            if (await containers.GetAsync(receipt.Provider, service.ResourceId, cancellationToken) is not null)
                throw new InvalidOperationException("Test service remains.");
        }
        if (receipt.OwnedNetworkId is { } network)
        {
            await sandbox.DeleteIsolatedNetworkAsync(network, cancellationToken);
            if (await sandbox.IsolatedNetworkExistsAsync(network, cancellationToken)) throw new InvalidOperationException("Test network remains.");
        }
    }
    public Task<ContainerRuntimeStatus?> GetStatusAsync(ContainerDeploymentReceipt receipt, CancellationToken cancellationToken) => Task.FromResult<ContainerRuntimeStatus?>(new(receipt.ProjectName, RuntimeStatus.Running, receipt.Services));
    public Task<ContainerExecResult> ExecAsync(ContainerDeploymentReceipt receipt, string serviceName, IReadOnlyList<string> command, TimeSpan timeout, CancellationToken cancellationToken) =>
        sandbox.ExecAsync(NamedContainerRuntime.ServiceReceipt(receipt, receipt.Services.Single(service => service.Name == serviceName)), command, timeout, cancellationToken);
}
