using DotNet.Testcontainers.Builders;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker;
using NoCTF.Runtime.Docker.Services;
using NoCTF.Runtime.Docker.Containers;
using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class DockerWorkloadInventoryTests
{
    [Test, Arguments(false), Arguments(true), Timeout(300_000)]
    public async Task Creation_replay_preserves_the_original_job_kind_and_container(bool templateTest, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var image = new ContainerBuilder("busybox:1.36.1").WithCommand("true").Build();
            await image.StartAsync(ct);
            var id = Guid.NewGuid();
            var endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST")
                ?? (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock");
            using var lifecycle = new DockerContainerLifecycle(new(Endpoint: endpoint, NetworkName: "none"));
            var labels = new Dictionary<string, string>
            {
                ["noctf.io/managed"] = "true", ["noctf.io/runtime-instance-id"] = id.ToString("D"),
                ["noctf.io/job-kind"] = templateTest ? "challenge-test-runtime" : "persistent-runtime"
            };
            var request = new ContainerRequest(id, RuntimeProvider.Docker, "busybox:1.36.1", ["sleep", "120"], new Dictionary<string, string>(), labels, new Dictionary<int, int>(), new(64 * 1024 * 1024, 200, 64), null, RuntimeInstanceId: id);
            var receipt = await lifecycle.CreateAsync(request, ct);
            try
            {
                await Assert.That((await lifecycle.EnsureRunningAsync(request, ct)).ResourceId).IsEqualTo(receipt.ResourceId);
                var wrongLabels = new Dictionary<string, string>(labels) { ["noctf.io/job-kind"] = "another-purpose" };
                await Assert.That(async () => await lifecycle.EnsureRunningAsync(request with { Labels = wrongLabels }, ct)).Throws<InvalidOperationException>();
                await Assert.That((await lifecycle.GetAsync(RuntimeProvider.Docker, receipt.ResourceId, ct))!.Status).IsEqualTo(RuntimeStatus.Running);
            }
            finally { await lifecycle.DestroyAsync(receipt, CancellationToken.None); }
        });
    }

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
            using var inventory = new DockerRuntimeResourceReconciler(options);
            var identity = new RuntimeWorkloadIdentity(RuntimeWorkloadKind.AwdChecker, runtimeId, operationId);
            await Assert.That(await inventory.WorkloadExistsAsync(identity, ct)).IsTrue();
            await checker.DisposeAsync();
            await Assert.That(await inventory.WorkloadExistsAsync(identity, ct)).IsFalse();
            await Assert.That(await inventory.WorkloadExistsAsync(new(RuntimeWorkloadKind.Runtime, runtimeId, runtimeId), ct))
                .IsTrue();
        });
    }
}
