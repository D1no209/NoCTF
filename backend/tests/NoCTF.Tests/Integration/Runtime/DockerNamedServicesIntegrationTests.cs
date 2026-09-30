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
public sealed class DockerNamedServicesIntegrationTests
{
    [Test, Timeout(300_000)]
    public async Task Single_service_reuses_shared_bridge_while_named_groups_are_isolated_and_publish_native_ports(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var dependency = new ContainerBuilder("nginx:alpine").Build();
            await dependency.StartAsync(ct);
            var endpoint = OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock";
            using var client = new DockerClientBuilder().WithEndpoint(new Uri(endpoint)).Build();
            var sharedName = $"noctf-it-shared-{Guid.NewGuid():N}";
            var shared = await client.Networks.CreateNetworkAsync(new() { Name = sharedName, Driver = "bridge" }, ct);
            var options = new DockerRuntimeOptions(Endpoint: endpoint, NetworkName: sharedName, PublicHost: dependency.Hostname,
                RegistryConfigDirectory: Path.Combine(Path.GetTempPath(), $"noctf-anonymous-registry-{Guid.NewGuid():N}"));
            using var containers = new DockerContainerLifecycle(options);
            var runtime = new DockerContainerRuntime(containers, options);
            var receipts = new List<ContainerDeploymentReceipt>();
            try
            {
                var single = await runtime.UpAsync(Request([new("main", "nginx:alpine")]), ct);
                receipts.Add(single);
                await Assert.That(single.OwnedNetworkId).IsNull();
                var inspected = await client.Containers.InspectContainerAsync(single.Services[0].ResourceId, ct);
                await Assert.That(inspected.NetworkSettings!.Networks[sharedName].NetworkID).IsEqualTo(shared.ID);
                var replay = await runtime.UpAsync(Request([new("main", "nginx:alpine")]) with { OperationId = single.OperationId }, ct);
                await Assert.That(replay.Services[0].ResourceId).IsEqualTo(single.Services[0].ResourceId);
                RuntimeServiceDefinition[] services = [new("web", "nginx:alpine"), new("db", "alpine:latest", Command: ["sleep"], Arguments: ["300"])];
                var first = await runtime.UpAsync(Request(services), ct); receipts.Add(first);
                var second = await runtime.UpAsync(Request(services), ct); receipts.Add(second);
                await Assert.That(first.OwnedNetworkId).IsNotNull();
                await Assert.That(first.OwnedNetworkId).IsNotEqualTo(second.OwnedNetworkId);
                var result = await runtime.ExecAsync(first, "web", ["sh", "-c", "getent hosts db"], TimeSpan.FromSeconds(5), ct);
                await Assert.That(result.ExitCode).IsEqualTo(0);
                using var http = new HttpClient();
                foreach (var receipt in receipts)
                {
                    var web = receipt.Services[0];
                    var port = web.PublishedPorts[80];
                    await Assert.That(port).IsGreaterThan(0);
                    using var response = await http.GetAsync($"http://{receipt.PublicHost}:{port}/", ct);
                    await Assert.That(response.IsSuccessStatusCode).IsTrue();
                }
                foreach (var receipt in receipts) await runtime.DownAsync(receipt, ct);
                receipts.Clear();
                await Assert.That((await client.Networks.InspectNetworkAsync(shared.ID, ct)).Name).IsEqualTo(sharedName);
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                foreach (var receipt in receipts) await runtime.DownAsync(receipt, RuntimeTerminationMode.Force, RuntimeTerminationPolicy.Default, cleanup.Token);
                await client.Networks.DeleteNetworkAsync(shared.ID, cleanup.Token);
            }
        });
    }

    private static ContainerRuntimeRequest Request(RuntimeServiceDefinition[] services) => new(Guid.NewGuid(), RuntimeProvider.Docker,
        services, new Dictionary<string, string>(), RuntimeResourceBudgetPolicy.Sum(services.Select(service => service.Resources(256))),
        null, TimeSpan.FromMinutes(2), [new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 80, services[0].Name)]);
}
