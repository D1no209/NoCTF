using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker.Containers;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
[NotInParallel]
public sealed class DockerContainerLifecycleTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Isolated_network_replay_returns_the_original_network(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var dockerProbe = new ContainerBuilder("alpine:3.20")
                .WithCommand("true")
                .Build();
            await dockerProbe.StartAsync(cancellationToken);
            using var lifecycle = CreateLifecycle();
            var operationId = Guid.NewGuid();
            var first = await lifecycle.CreateIsolatedNetworkAsync(
                new RuntimeResourceIdentity(operationId, 1, 8080),
                DateTimeOffset.UtcNow.AddMinutes(1), cancellationToken);
            try
            {
                var replay = await lifecycle.CreateIsolatedNetworkAsync(
                    new RuntimeResourceIdentity(operationId, 1, 8080),
                    DateTimeOffset.UtcNow.AddMinutes(1), cancellationToken);

                await Assert.That(replay).IsEqualTo(first);
            }
            finally
            {
                await lifecycle.DeleteIsolatedNetworkAsync(first, cancellationToken);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task ExecWithInputAsync_WritesStdinWithoutStoppingContainer(CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new ContainerBuilder("alpine:3.20")
                .WithCommand("sleep", "300")
                .Build();
            await container.StartAsync(cancellationToken);
            using var lifecycle = CreateLifecycle();
            var receipt = Receipt(container.Id);

            var result = await lifecycle.ExecWithInputAsync(receipt,
                ["/bin/sh", "-c", "read value; test \"$value\" = protected-input"],
                Encoding.UTF8.GetBytes("protected-input\n"), TimeSpan.FromSeconds(5), cancellationToken);
            var probe = await lifecycle.ExecAsync(receipt, ["/bin/true"], TimeSpan.FromSeconds(5), cancellationToken);

            await Assert.That(result).IsEqualTo(new ContainerExecResult(0, false));
            await Assert.That(probe).IsEqualTo(new ContainerExecResult(0, false));
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task ExecAsync_TimeoutStopsContainerAndProcessTree(CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var container = new ContainerBuilder("alpine:3.20")
                .WithCommand("sleep", "300")
                .Build();
            await container.StartAsync(cancellationToken);
            using var lifecycle = CreateLifecycle();
            var receipt = Receipt(container.Id);

            var result = await lifecycle.ExecAsync(receipt,
                ["/bin/sh", "-c", "echo $$ > /tmp/noctf-timeout.pid; trap '' TERM; while true; do :; done"],
                TimeSpan.FromMilliseconds(200), cancellationToken);
            var stopped = await lifecycle.GetAsync(RuntimeProvider.Docker, container.Id, cancellationToken);

            await Assert.That(result.TimedOut).IsTrue();
            await Assert.That(stopped).IsNotNull();
            await Assert.That(stopped!.Status).IsEqualTo(RuntimeStatus.Stopped);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Checkers_use_distinct_internal_callback_networks_without_platform_access(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var callback = new ContainerBuilder("alpine:3.20")
                .WithCommand("sleep", "300")
                .WithLabel("noctf.io/internal-role", "awdp-callback-gateway")
                .Build();
            await callback.StartAsync(cancellationToken);
            var endpoint = DockerEndpoint();
            using var docker = new DockerClientBuilder().WithEndpoint(new Uri(endpoint)).Build();
            using var lifecycle = new DockerContainerLifecycle(new DockerRuntimeOptions(
                endpoint, "noctf-platform", "localhost", callback.Id));
            var firstOperationId = Guid.NewGuid();
            var secondOperationId = Guid.NewGuid();
            var firstSandbox = await lifecycle.CreateIsolatedNetworkAsync(
                new RuntimeResourceIdentity(firstOperationId, 1, 8080),
                DateTimeOffset.UtcNow.AddMinutes(1), cancellationToken);
            var secondSandbox = await lifecycle.CreateIsolatedNetworkAsync(
                new RuntimeResourceIdentity(secondOperationId, 1, 8080),
                DateTimeOffset.UtcNow.AddMinutes(1), cancellationToken);
            ContainerReceipt? first = null;
            ContainerReceipt? second = null;
            try
            {
                first = await lifecycle.CreateAsync(
                    CheckerRequest(firstOperationId, firstSandbox), cancellationToken);
                second = await lifecycle.CreateAsync(
                    CheckerRequest(secondOperationId, secondSandbox), cancellationToken);
                var firstCallback = await docker.Networks.InspectNetworkAsync(
                    $"noctf-callback-{firstOperationId:N}", cancellationToken);
                var secondCallback = await docker.Networks.InspectNetworkAsync(
                    $"noctf-callback-{secondOperationId:N}", cancellationToken);
                await Assert.That(firstCallback.Internal).IsTrue();
                await Assert.That(secondCallback.Internal).IsTrue();
                await Assert.That(firstCallback.ID).IsNotEqualTo(secondCallback.ID);
                await Assert.That(firstCallback.Containers.Keys)
                    .IsEquivalentTo([callback.Id, first.ResourceId]);
                await Assert.That(secondCallback.Containers.Keys)
                    .IsEquivalentTo([callback.Id, second.ResourceId]);
                var inspected = await docker.Containers.InspectContainerAsync(first.ResourceId, cancellationToken);
                await Assert.That(inspected.NetworkSettings!.Networks.Keys.Contains("noctf-platform"))
                    .IsFalse();
            }
            finally
            {
                if (first is not null)
                    await lifecycle.DestroyAsync(first, cancellationToken);
                if (second is not null)
                    await lifecycle.DestroyAsync(second, cancellationToken);
                await lifecycle.DeleteIsolatedNetworkAsync(firstSandbox, cancellationToken);
                await lifecycle.DeleteIsolatedNetworkAsync(secondSandbox, cancellationToken);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Callback_network_failure_removes_container_created_before_receipt(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var dockerProbe = new ContainerBuilder("alpine:3.20")
                .WithCommand("true")
                .Build();
            await dockerProbe.StartAsync(cancellationToken);
            var operationId = Guid.NewGuid();
            using var lifecycle = new DockerContainerLifecycle(new DockerRuntimeOptions(
                DockerEndpoint(), "noctf-platform", "localhost", $"missing-{Guid.NewGuid():N}"));
            var sandbox = await lifecycle.CreateIsolatedNetworkAsync(
                new RuntimeResourceIdentity(operationId, 1, 8080),
                DateTimeOffset.UtcNow.AddMinutes(1), cancellationToken);
            Func<Task> action = () => lifecycle.CreateAsync(
                CheckerRequest(operationId, sandbox), cancellationToken);

            try
            {
                await Assert.That(action).ThrowsException();
                var remaining = await lifecycle.GetAsync(
                    RuntimeProvider.Docker, $"noctf-{operationId:N}", cancellationToken);
                await Assert.That(remaining).IsNull();
            }
            finally
            {
                await lifecycle.DeleteIsolatedNetworkAsync(sandbox, cancellationToken);
            }
        });
    }

    private static DockerContainerLifecycle CreateLifecycle() => new(new DockerRuntimeOptions(DockerEndpoint()));

    private static string DockerEndpoint() => Environment.GetEnvironmentVariable("DOCKER_HOST") ??
        (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock");

    private static ContainerRequest CheckerRequest(Guid operationId, string? networkName) => new(
        operationId,
        RuntimeProvider.Docker,
        "alpine:3.20",
        ["sleep", "300"],
        new Dictionary<string, string>
        {
            ["NOCTF_CALLBACK_URL"] = "http://callback:8080/api/internal/v1/awdp/fix-results"
        },
        new Dictionary<string, string> { ["noctf.purpose"] = "awdp-checker" },
        new Dictionary<int, int>(),
        new RuntimeResourceLimits(128 * 1024 * 1024, 100_000_000, 64),
        new ContainerSecurityPolicy(true, false, true, ["ALL"], []),
        TimeSpan.FromMinutes(1),
        NetworkName: networkName,
        AllowInternalCallback: true,
        Generation: 1);

    private static ContainerReceipt Receipt(string resourceId) => new(
        Guid.NewGuid(), RuntimeProvider.Docker, resourceId, RuntimeStatus.Running,
        new Dictionary<int, int>(), "localhost", null);
}
