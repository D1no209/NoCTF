using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker.Containers;
using NoCTF.Runtime.Docker.Services;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class DockerExecutionIsolationIntegrationTests
{
    private const string WebImage = "nginx@sha256:df221db836e1754089190208cee7eeda94f233197056426eda74a43ab1abeac2";
    private const string ToolImage = "alpine@sha256:294b683cb724975bec92580e1e685676bd4b50bda910ddb8c51d4cabeaec77e6";

    [Test, Timeout(300_000)]
    public async Task Deployment_execution_bridge_blocks_peer_access_but_the_host_gateway_can_reach_both_scopes_without_public_ports(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var gateway = new ContainerBuilder(ToolImage)
                .WithCreateParameterModifier(parameters => { parameters.HostConfig ??= new(); parameters.HostConfig.NetworkMode = "host"; parameters.NetworkingConfig = null; })
                .WithLabel("noctf.io/execution-proxy-gateway", "true").WithCommand("sleep", "300").Build();
            await gateway.StartAsync(ct);
            var endpoint = OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock";
            using var client = new DockerClientBuilder().WithEndpoint(new Uri(endpoint)).Build();
            var name = $"noctf-it-execution-{Guid.NewGuid():N}";
            var bridge = await client.Networks.CreateNetworkAsync(new() { Name = name, Driver = "bridge",
                Options = new Dictionary<string, string> { ["com.docker.network.bridge.enable_icc"] = "false" } }, ct);
            var options = new DockerRuntimeOptions(Endpoint: endpoint, NetworkName: "unused-ordinary-network",
                ExecutionNetworkName: name, ExecutionProxyContainerName: gateway.Id,
                RegistryConfigDirectory: Path.Combine(Path.GetTempPath(), $"noctf-it-registry-{Guid.NewGuid():N}"));
            using var lifecycle = new DockerContainerLifecycle(options);
            var runtime = new DockerContainerRuntime(lifecycle, options); var receipts = new List<ContainerDeploymentReceipt>();
            try
            {
                var first = await runtime.UpAsync(Request(), ct); receipts.Add(first);
                var second = await runtime.UpAsync(Request(), ct); receipts.Add(second);
                foreach (var receipt in receipts)
                {
                    await Assert.That(receipt.IsolationState).IsEqualTo(RuntimeIsolationState.Verified);
                    await Assert.That(receipt.OwnedNetworkId).IsNull();
                    await Assert.That(receipt.Services.Single().PublishedPorts.Count).IsEqualTo(0);
                    var inspected = await client.Containers.InspectContainerAsync(receipt.Services.Single().ResourceId, ct);
                    var networks = inspected.NetworkSettings?.Networks ?? throw new InvalidOperationException("Missing container network metadata.");
                    await Assert.That(networks[name].NetworkID).IsEqualTo(bridge.ID);
                    var reachable = await gateway.ExecAsync(["wget", "-q", "-T", "3", "-O", "-", $"http://{receipt.Services.Single().InternalHost}/"], ct);
                    await Assert.That(reachable.ExitCode).IsEqualTo(0);
                }
                var denied = await runtime.ExecAsync(first, "web", ["wget", "-q", "-T", "2", "-O", "-", $"http://{second.Services.Single().InternalHost}/"],
                    TimeSpan.FromSeconds(5), ct);
                await Assert.That(denied.ExitCode).IsNotEqualTo(0);
                var replay = await runtime.UpAsync(Request() with { OperationId = first.OperationId, ExecutionScopeId = first.ExecutionScopeId }, ct);
                await Assert.That(replay.Services.Single().ResourceId).IsEqualTo(first.Services.Single().ResourceId);
                await Assert.That(replay.IsolationState).IsEqualTo(RuntimeIsolationState.Verified);
                RuntimeServiceDefinition[] group = [new("web", WebImage), new("db", WebImage)];
                var grouped = await runtime.UpAsync(Request() with { Services = group,
                    Limits = RuntimeResourceBudgetPolicy.Sum(group.Select(x => x.Resources(256))) }, ct);
                receipts.Add(grouped);
                await Assert.That(grouped.OwnedNetworkId).IsNotNull();
                await Assert.That(grouped.IsolationState).IsEqualTo(RuntimeIsolationState.Verified);
                var discovery = await runtime.ExecAsync(grouped, "web", ["wget", "-q", "-T", "2", "-O", "-", "http://db/"], TimeSpan.FromSeconds(5), ct);
                await Assert.That(discovery.ExitCode).IsEqualTo(0);
                var deniedGroup = await runtime.ExecAsync(first, "web", ["wget", "-q", "-T", "2", "-O", "-", $"http://{grouped.Services.Single(x => x.Name == "web").InternalHost}/"],
                    TimeSpan.FromSeconds(5), ct);
                await Assert.That(deniedGroup.ExitCode).IsNotEqualTo(0);
                var deniedSingle = await runtime.ExecAsync(grouped, "web", ["wget", "-q", "-T", "2", "-O", "-", $"http://{second.Services.Single().InternalHost}/"],
                    TimeSpan.FromSeconds(5), ct);
                await Assert.That(deniedSingle.ExitCode).IsNotEqualTo(0);
                await Assert.That(async () => await runtime.UpAsync(Request() with { AccessMode = RuntimeAccessMode.Direct }, ct))
                    .Throws<RuntimeConfigurationException>();
                foreach (var receipt in receipts) await runtime.DownAsync(receipt, ct);
                receipts.Clear();
                await Assert.That((await client.Networks.InspectNetworkAsync(bridge.ID, ct)).Name).IsEqualTo(name);
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                foreach (var receipt in receipts) await runtime.DownAsync(receipt, RuntimeTerminationMode.Force, RuntimeTerminationPolicy.Default, cleanup.Token);
                await client.Networks.DeleteNetworkAsync(bridge.ID, cleanup.Token);
            }
        });
    }

    [Test, Timeout(300_000)]
    public async Task An_ordinary_bridge_or_container_network_gateway_cannot_be_accepted_as_verified_isolation(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var gateway = new ContainerBuilder(ToolImage).WithLabel("noctf.io/execution-proxy-gateway", "true")
                .WithCommand("sleep", "300").Build();
            await gateway.StartAsync(ct);
            var endpoint = OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock";
            using var client = new DockerClientBuilder().WithEndpoint(new Uri(endpoint)).Build();
            var name = $"noctf-it-unverified-{Guid.NewGuid():N}";
            var bridge = await client.Networks.CreateNetworkAsync(new() { Name = name, Driver = "bridge" }, ct);
            try
            {
                using var lifecycle = new DockerContainerLifecycle(new(Endpoint: endpoint, ExecutionNetworkName: name, ExecutionProxyContainerName: gateway.Id));
                await Assert.That(async () => await lifecycle.EnsureExecutionNetworkAsync(ct)).Throws<RuntimeConfigurationException>();
                var isolated = await client.Networks.CreateNetworkAsync(new() { Name = name + "-icc", Driver = "bridge",
                    Options = new Dictionary<string, string> { ["com.docker.network.bridge.enable_icc"] = "false" } }, ct);
                try
                {
                    using var wrongGateway = new DockerContainerLifecycle(new(Endpoint: endpoint, ExecutionNetworkName: name + "-icc", ExecutionProxyContainerName: gateway.Id));
                    await Assert.That(async () => await wrongGateway.EnsureExecutionNetworkAsync(ct)).Throws<RuntimeConfigurationException>();
                }
                finally { await client.Networks.DeleteNetworkAsync(isolated.ID, ct); }
            }
            finally { await client.Networks.DeleteNetworkAsync(bridge.ID, ct); }
        });
    }

    private static ContainerRuntimeRequest Request()
    {
        RuntimeServiceDefinition[] services = [new("web", WebImage)];
        return new(Guid.NewGuid(), RuntimeProvider.Docker, services, new Dictionary<string, string>(),
            RuntimeResourceBudgetPolicy.Sum(services.Select(x => x.Resources(256))), null, TimeSpan.FromSeconds(30),
            [new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 80, "web")], AccessMode: RuntimeAccessMode.WsrxOnly, ExecutionScopeId: Guid.NewGuid());
    }
}
