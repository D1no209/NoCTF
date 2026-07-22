using System.Text;
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
    public async Task ExecWithInputAsync_WritesStdinWithoutStoppingContainer(CancellationToken cancellationToken)
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
    }

    [Test]
    [Timeout(300_000)]
    public async Task ExecAsync_TimeoutStopsContainerAndProcessTree(CancellationToken cancellationToken)
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
    }

    private static DockerContainerLifecycle CreateLifecycle() => new(new DockerRuntimeOptions(
        Environment.GetEnvironmentVariable("DOCKER_HOST") ??
        (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock")));

    private static ContainerReceipt Receipt(string resourceId) => new(
        Guid.NewGuid(), RuntimeProvider.Docker, resourceId, RuntimeStatus.Running,
        new Dictionary<int, int>(), "localhost", null);
}
