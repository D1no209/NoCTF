using Docker.DotNet.Models;
using Docker.DotNet;
using System.Net;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runtime.Docker.Containers;

public sealed partial class DockerContainerLifecycle
{
    private readonly SemaphoreSlim executionProbeGate = new(1, 1);
    private string? probedExecutionNetwork;
    private DateTimeOffset executionProbedAt;

    public async Task<string> EnsureExecutionNetworkAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.ExecutionNetworkName) || options.ExecutionNetworkName == options.NetworkName
            || options.ExecutionNetworkName == options.CallbackNetworkName)
            throw new RuntimeConfigurationException("A distinct deployment-owned execution bridge must be configured.");
        var network = await client.Networks.InspectNetworkAsync(options.ExecutionNetworkName, ct);
        if (network.Driver != "bridge" || network.Internal || network.EnableIPv6
            || !MatchesMetadata(network.Options, "com.docker.network.bridge.enable_icc", "false"))
            throw new RuntimeConfigurationException("The execution bridge must disable inter-container communication.");
        await VerifyExecutionGatewayAsync(ct);
        await VerifyExecutionConnectivityAsync(network.ID, ct);
        return network.Name;
    }

    private async Task VerifyExecutionGatewayAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.ExecutionProxyContainerName))
            throw new RuntimeConfigurationException("A host-network execution proxy gateway must be configured.");
        var gateway = await client.Containers.InspectContainerAsync(options.ExecutionProxyContainerName, ct);
        if (gateway.State?.Running != true || gateway.HostConfig?.NetworkMode != "host"
            || !MatchesMetadata(gateway.Config?.Labels, "noctf.io/execution-proxy-gateway", "true"))
            throw new RuntimeConfigurationException("The execution gateway must be running in the Docker host network with its deployment role.");
    }

    private async Task ValidateExecutionContainerNetworkAsync(ContainerRequest request, CancellationToken ct)
    {
        if (request.ExecutionScopeId is null || request.ExecutionScopeId == Guid.Empty)
            throw new RuntimeConfigurationException("An execution scope is required.");
        if (request.AccessMode != RuntimeAccessMode.WsrxOnly || request.PortMappings.Count != 0 || request.AllowInternalCallback)
            throw new RuntimeConfigurationException("Controlled executions cannot publish host ports or attach callbacks.");
        if (string.IsNullOrWhiteSpace(request.NetworkName))
            throw new RuntimeConfigurationException("The execution network is required.");
        var shared = await EnsureExecutionNetworkAsync(ct);
        if (!request.RegisterServiceAlias && request.NetworkName != shared)
            throw new RuntimeConfigurationException("A single-service execution must use the deployment execution bridge.");
        if (request.RegisterServiceAlias)
        {
            var network = await client.Networks.InspectNetworkAsync(request.NetworkName, ct);
            if (network.Driver != "bridge" || network.Name != $"noctf-rt-{request.OperationId:N}"
                || network.EnableIPv6
                || !HasResourceIdentity(network.Labels, new(request.OperationId)))
                throw new RuntimeConfigurationException("A service group must use its own Runtime bridge.");
        }
    }

    public async Task VerifyExecutionDeploymentAsync(ContainerRuntimeRequest request, ContainerDeploymentReceipt receipt, CancellationToken ct)
    {
        await EnsureExecutionNetworkAsync(ct);
        foreach (var service in receipt.Services)
        {
            var container = await client.Containers.InspectContainerAsync(service.ResourceId, ct);
            if (container.State?.Running != true || !MatchesMetadata(container.Config?.Labels, "noctf.io/execution-scope-id", request.ExecutionScopeId?.ToString("D"))
                || !DropsRawNetworking(container.HostConfig?.CapDrop)
                || !HasPublishedPortBindings(container.NetworkSettings?.Ports, new Dictionary<int, int>())
                || container.NetworkSettings?.Networks?.Count != 1)
                throw new RuntimeConfigurationException("The execution container does not satisfy the verified network and capability contract.");
            var network = container.NetworkSettings.Networks.Single();
            var expected = request.Services.Count == 1 ? options.ExecutionNetworkName : receipt.OwnedNetworkId;
            if (network.Key != expected && network.Value.NetworkID != expected)
                throw new RuntimeConfigurationException("The execution container is attached to an unexpected network.");
        }
    }

    private static bool MatchesMetadata(IDictionary<string, string>? metadata, string key, string? expected) =>
        metadata is not null && metadata.TryGetValue(key, out var actual) && actual == expected;

    private static bool DropsRawNetworking(IList<string>? capabilities) =>
        capabilities?.Any(value => value.ToUpperInvariant() is "NET_RAW" or "CAP_NET_RAW" or "ALL") == true;

    private async Task VerifyExecutionConnectivityAsync(string networkId, CancellationToken ct)
    {
        if (probedExecutionNetwork == networkId && timeProvider.GetUtcNow() - executionProbedAt < TimeSpan.FromSeconds(5)) return;
        await executionProbeGate.WaitAsync(ct);
        var probes = new List<string>();
        var passed = false;
        try
        {
            if (probedExecutionNetwork == networkId && timeProvider.GetUtcNow() - executionProbedAt < TimeSpan.FromSeconds(5)) return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
            await EnsureImageAvailableAsync(options.ExecutionProbeImage, timeout.Token);
            async Task<string> Start(bool host)
            {
                var name = $"noctf-execution-probe-{Guid.NewGuid():N}";
                probes.Add(name);
                var created = await client.Containers.CreateContainerAsync(new()
                {
                    Name = name,
                    Image = options.ExecutionProbeImage,
                    Entrypoint = ["/bin/sh", "-c"],
                    Cmd = host ? ["exec sleep 30"] : ["printf 'events {} http { server { listen 8080; location / { return 200 ready; } } }' >/tmp/noctf-probe.conf; exec timeout 30 nginx -c /tmp/noctf-probe.conf -g 'daemon off;'"],
                    Labels = new Dictionary<string, string> { ["noctf.io/internal-role"] = "execution-isolation-probe" },
                    HostConfig = new() { NetworkMode = host ? "host" : networkId, CapDrop = ["NET_RAW"], SecurityOpt = ["no-new-privileges:true"],
                        Memory = 64 * 1024 * 1024, NanoCPUs = 50_000_000, PidsLimit = 32 }
                }, timeout.Token);
                await client.Containers.StartContainerAsync(created.ID, new(), timeout.Token); return created.ID;
            }
            var left = await Start(false); var right = await Start(false); var host = await Start(true);
            var destination = ResolveInternalAddress(await client.Containers.InspectContainerAsync(right, timeout.Token), networkId);
            var command = new[] { "wget", "-q", "-T", "1", "-O", "-", $"http://{destination}:8080/" };
            Task<ContainerExecResult> Check(string id) => RunExecAsync(new(Guid.Empty, RuntimeProvider.Docker, id, RuntimeStatus.Running,
                new Dictionary<int, int>(), null, null), command, null, TimeSpan.FromSeconds(3), timeout.Token);
            var reachable = await Check(host);
            var peer = await Check(left);
            if (reachable.ExitCode != 0 || reachable.TimedOut || peer.ExitCode == 0 || peer.TimedOut)
                throw new RuntimeConfigurationException("Execution network connectivity self-test failed; isolation cannot be certified.");
            passed = true;
        }
        finally
        {
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                foreach (var probe in probes)
                {
                    try { await client.Containers.RemoveContainerAsync(probe, new() { Force = true }, cleanup.Token); }
                    catch (DockerApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound) { }
                }
                if (passed) { probedExecutionNetwork = networkId; executionProbedAt = timeProvider.GetUtcNow(); }
            }
            finally { executionProbeGate.Release(); }
        }
    }
}
