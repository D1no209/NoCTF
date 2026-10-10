using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker.Containers;

namespace NoCTF.Runtime.Docker.Services;

/// <summary>Creates native Docker containers and a bridge only for multi-service environments.</summary>
public sealed class DockerContainerRuntime(DockerContainerLifecycle containers, DockerRuntimeOptions options, TimeProvider? clock = null)
    : NamedContainerRuntime(containers, containers, clock)
{
    protected override RuntimeProvider Provider => RuntimeProvider.Docker;
    protected override string PublicHost => options.PublicHost;
    protected override string Namespace => string.Empty;

    protected override async Task<RuntimeNetworkAttachment> PrepareNetworkAsync(ContainerRuntimeRequest request, CancellationToken cancellationToken)
    {
        if (request.EgressPolicy != RuntimeEgressPolicy.Isolated)
            throw new RuntimeConfigurationException("Docker does not support InternetOnly egress.");
        if (request.ExecutionScopeId is not null)
        {
            if (request.ExecutionScopeId == Guid.Empty || request.AccessMode != RuntimeAccessMode.WsrxOnly || request.AllowInternalCallback)
                throw new RuntimeConfigurationException("Controlled execution requires an opaque scope and WSRX-only access without callbacks.");
            var executionNetwork = await containers.EnsureExecutionNetworkAsync(cancellationToken);
            if (request.Services.Count == 1) return new(executionNetwork);
        }
        if (request.Services.Count == 1)
        {
            await containers.EnsureSharedRuntimeNetworkAsync(request.ControlCheckUrlBinding is null ? request.AccessMode : RuntimeAccessMode.DirectAndWsrx, cancellationToken);
            return new(options.NetworkName);
        }
        var network = await containers.CreateIsolatedNetworkAsync(new(new(request.OperationId), request.Purpose,
            request.EgressPolicy, [], request.Purpose == ContainerNetworkPurpose.AwdpVerification
                ? request.Services.Single().InternalPorts!.Single() : null), cancellationToken);
        return new(network, network);
    }

    protected override Task VerifyExecutionIsolationAsync(ContainerRuntimeRequest request, ContainerDeploymentReceipt receipt, CancellationToken ct) =>
        request.ExecutionScopeId is null ? Task.CompletedTask : containers.VerifyExecutionDeploymentAsync(request, receipt, ct);

    protected override async Task RemoveNetworkAsync(ContainerDeploymentReceipt receipt, CancellationToken cancellationToken)
    {
        if (receipt.OwnedNetworkId is null) return;
        await containers.DisconnectProxyGatewaysAsync(receipt.OwnedNetworkId, cancellationToken);
        await containers.DeleteIsolatedNetworkAsync(receipt.OwnedNetworkId, cancellationToken);
        if (await containers.IsolatedNetworkExistsAsync(receipt.OwnedNetworkId, cancellationToken))
            throw new InvalidOperationException("Runtime network remains after cleanup.");
    }
}
