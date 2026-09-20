using Docker.DotNet;
using DotNet.Testcontainers.Builders;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker.Containers;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class DockerBudgetLimitTests
{
    [Test, Timeout(300_000)]
    public async Task Startup_reservation_matches_the_actual_Docker_hard_limit(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var image = new ContainerBuilder("busybox:1.36.1").WithCommand("true").Build();
            await image.StartAsync(ct);
            var endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST")
                ?? (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock");
            var id = Guid.NewGuid();
            var limits = new RuntimeResourceLimits(64 * 1024 * 1024, 200_000_000, 64);
            var budget = new RuntimeResourceBudgetPolicy().Calculate(limits, RuntimeProvider.Docker);
            using var runtime = new DockerContainerLifecycle(new(Endpoint: endpoint, NetworkName: "none"));
            var request = new ContainerRequest(id, RuntimeProvider.Docker, "busybox:1.36.1", ["sleep", "120"],
                new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<int, int>(), limits,
                new(true, false, false, ["ALL"], []), null, Budget: budget);
            var receipt = await runtime.CreateAsync(request, ct);
            try
            {
                using var docker = new DockerClientBuilder().WithEndpoint(new Uri(endpoint)).Build();
                var actual = await docker.Containers.InspectContainerAsync(receipt.ResourceId, ct);
                var actualLimits = actual.HostConfig ?? throw new InvalidOperationException("Docker omitted the workload limits.");
                await Assert.That(actualLimits.Memory).IsEqualTo(limits.MemoryBytes);
                await Assert.That(actualLimits.NanoCPUs).IsEqualTo(limits.NanoCpus);
                await Assert.That(actualLimits.PidsLimit).IsEqualTo(limits.PidsLimit);
                await Assert.That(budget).IsEqualTo(limits);
            }
            finally { await runtime.DestroyAsync(receipt, CancellationToken.None); }
        });
    }
}
