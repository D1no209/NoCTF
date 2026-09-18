using DotNet.Testcontainers.Builders;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker;
using NoCTF.Runtime.Docker.Compose;
using NoCTF.Runtime.Docker.Containers;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class DockerWorkloadInventoryTests
{
    [Test, Timeout(300_000)]
    public async Task Checker_inventory_does_not_confuse_its_parent_Runtime_with_its_own_container(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            var runtimeId = Guid.NewGuid();
            var operationId = Guid.NewGuid();
            await using var parent = new ContainerBuilder("busybox:1.36.1")
                .WithName($"noctf-{runtimeId:N}").WithCommand("sleep", "120")
                .WithLabel("noctf.io/managed", "true")
                .WithLabel("noctf.io/runtime-instance-id", runtimeId.ToString("D"))
                .Build();
            await using var checker = new ContainerBuilder("busybox:1.36.1")
                .WithName($"noctf-{operationId:N}").WithCommand("sleep", "120")
                .WithLabel("noctf.io/managed", "true")
                .WithLabel("noctf.io/runtime-instance-id", runtimeId.ToString("D"))
                .WithLabel("noctf.io/operation-id", operationId.ToString("N"))
                .Build();
            await Task.WhenAll(parent.StartAsync(ct), checker.StartAsync(ct));
            var endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST")
                ?? (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock");
            var options = new DockerRuntimeOptions(Endpoint: endpoint);
            using var inventory = new DockerRuntimeResourceReconciler(options, new DockerComposeRuntime(options));
            var identity = new RuntimeWorkloadIdentity(RuntimeWorkloadKind.AwdChecker, runtimeId, operationId);
            await Assert.That(await inventory.WorkloadExistsAsync(identity, ct)).IsTrue();
            await checker.DisposeAsync();
            await Assert.That(await inventory.WorkloadExistsAsync(identity, ct)).IsFalse();
            await Assert.That(await inventory.WorkloadExistsAsync(new(RuntimeWorkloadKind.Runtime, runtimeId, runtimeId), ct))
                .IsTrue();
        });
    }
}
