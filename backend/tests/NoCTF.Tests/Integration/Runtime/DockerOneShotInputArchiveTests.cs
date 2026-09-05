using System.Formats.Tar;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker;
using NoCTF.Runtime.Docker.Containers;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
[NotInParallel]
public sealed class DockerOneShotInputArchiveTests
{
    [Test]
    [Timeout(300_000)]
    public async Task InputArchive_IsExtractedBeforeEntrypoint_AndContainerIsRemoved(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await EnsureBusyBoxAsync(cancellationToken);
            using var lifecycle = CreateLifecycle();
            using var docker = CreateClient();
            var operationId = Guid.NewGuid();
            await using var input = PrefixedCanonicalTar("initial-content");
            var oneShotInput = new OneShotInputArchive(
                input,
                OneShotInputArchive.RootDestinationPath);

            var result = await lifecycle.RunAsync(
                Request(
                    operationId,
                    "test -f /noctf/fix/fix.sh && cat /noctf/fix/payload.txt"),
                oneShotInput,
                cancellationToken);

            await Assert.That(result.ExitCode).IsEqualTo(0);
            await Assert.That(result.StandardOutput).IsEqualTo("initial-content");
            await Assert.That(input.CanRead).IsTrue();
            await Assert.That(oneShotInput.PreparationCompleted).IsTrue();
            Func<Task> inspect = async () =>
                _ = await docker.Containers.InspectContainerAsync(
                    $"noctf-{operationId:N}",
                    cancellationToken);
            await Assert.That(inspect).Throws<DockerContainerNotFoundException>();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task CheckerMutation_DoesNotChangeTargetCopy_AndLeavesNoManagedContainer(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await EnsureBusyBoxAsync(cancellationToken);
            using var lifecycle = CreateLifecycle();
            using var docker = CreateClient();
            var targetOperationId = Guid.NewGuid();
            var checkerOperationId = Guid.NewGuid();
            ContainerReceipt? target = null;
            try
            {
                target = await lifecycle.CreateAsync(
                    Request(targetOperationId, "sleep 300"),
                    cancellationToken);
                await using (var targetInput = PrefixedCanonicalTar("initial-content"))
                {
                    await lifecycle.CopyArchiveAsync(
                        target,
                        targetInput,
                        cancellationToken);
                }
                await using var checkerInput = PrefixedCanonicalTar("initial-content");

                var checker = await lifecycle.RunAsync(
                    Request(
                        checkerOperationId,
                        "printf changed > /noctf/fix/payload.txt; cat /noctf/fix/payload.txt"),
                    new OneShotInputArchive(
                        checkerInput,
                        OneShotInputArchive.RootDestinationPath),
                    cancellationToken);
                var targetProbe = await lifecycle.ExecAsync(
                    target,
                    ["/bin/sh", "-c", "test \"$(cat /noctf/fix/payload.txt)\" = initial-content"],
                    TimeSpan.FromSeconds(10),
                    cancellationToken);

                await Assert.That(checker.StandardOutput).IsEqualTo("changed");
                await Assert.That(targetProbe.ExitCode).IsEqualTo(0);
                var managed = await docker.Containers.ListContainersAsync(
                    new ContainersListParameters
                    {
                        All = true,
                        Filters = new Dictionary<string, IDictionary<string, bool>>
                        {
                            ["name"] = new Dictionary<string, bool>
                            {
                                [$"noctf-{checkerOperationId:N}"] = true
                            }
                        }
                    },
                    cancellationToken);
                await Assert.That(managed).IsEmpty();
            }
            finally
            {
                if (target is not null)
                    await lifecycle.DestroyAsync(target, CancellationToken.None);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task InvalidInputArchive_IsRejectedAndCreatedContainerIsRemoved(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await EnsureBusyBoxAsync(cancellationToken);
            using var lifecycle = CreateLifecycle();
            using var docker = CreateClient();
            var operationId = Guid.NewGuid();
            await using var input = new MemoryStream("not-a-tar"u8.ToArray());

            Func<Task> action = async () => _ = await lifecycle.RunAsync(
                Request(operationId, "true"),
                new OneShotInputArchive(input, OneShotInputArchive.RootDestinationPath),
                cancellationToken);

            await Assert.That(action).Throws<OneShotInputPreparationException>();
            Func<Task> inspect = async () =>
                _ = await docker.Containers.InspectContainerAsync(
                    $"noctf-{operationId:N}",
                    cancellationToken);
            await Assert.That(inspect).Throws<DockerContainerNotFoundException>();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task CancellationAfterInputPreparation_RemovesCheckerContainer(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await EnsureBusyBoxAsync(cancellationToken);
            using var lifecycle = CreateLifecycle();
            using var docker = CreateClient();
            var operationId = Guid.NewGuid();
            await using var input = PrefixedCanonicalTar("initial-content");
            using var canceled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            canceled.CancelAfter(TimeSpan.FromSeconds(2));

            Func<Task> action = async () => _ = await lifecycle.RunAsync(
                Request(operationId, "sleep 300"),
                new OneShotInputArchive(input, OneShotInputArchive.RootDestinationPath),
                canceled.Token);

            await Assert.That(action).Throws<OperationCanceledException>();
            Func<Task> inspect = async () =>
                _ = await docker.Containers.InspectContainerAsync(
                    $"noctf-{operationId:N}",
                    cancellationToken);
            await Assert.That(inspect).Throws<DockerContainerNotFoundException>();
        });
    }

    private static ContainerRequest Request(Guid operationId, string script) => new(
        operationId,
        RuntimeProvider.Docker,
        "busybox:1.36.1",
        ["/bin/sh", "-c", script],
        new Dictionary<string, string>(),
        new Dictionary<string, string>(),
        new Dictionary<int, int>(),
        new RuntimeResourceLimits(128 * 1024 * 1024, 100_000_000, 64),
        new ContainerSecurityPolicy(true, false, false, ["ALL"], []),
        TimeSpan.FromMinutes(2),
        NetworkName: "none",
        RuntimeInstanceId: operationId,
        NetworkPurpose: ContainerNetworkPurpose.AwdpVerification);

    private static MemoryStream PrefixedCanonicalTar(string payload)
    {
        using var tar = new MemoryStream();
        using (var writer = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "noctf/fix/fix.sh")
            {
                DataStream = new MemoryStream("#!/bin/sh\nexit 0\n"u8.ToArray())
            });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "noctf/fix/payload.txt")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(payload))
            });
        }
        var prefix = "ignored-prefix"u8.ToArray();
        var stream = new MemoryStream(prefix.Length + checked((int)tar.Length));
        stream.Write(prefix);
        tar.Position = 0;
        tar.CopyTo(stream);
        stream.Position = prefix.Length;
        return stream;
    }

    private static async Task EnsureBusyBoxAsync(CancellationToken cancellationToken)
    {
        await using var probe = new ContainerBuilder("busybox:1.36.1")
            .WithCommand("true")
            .Build();
        await probe.StartAsync(cancellationToken);
    }

    private static DockerContainerLifecycle CreateLifecycle() => new(new DockerRuntimeOptions(
        Endpoint: DockerEndpoint(),
        NetworkName: "none",
        PublicHost: "127.0.0.1"));

    private static DockerClient CreateClient() => new DockerClientBuilder()
        .WithEndpoint(new Uri(DockerEndpoint()))
        .Build();

    private static string DockerEndpoint() =>
        Environment.GetEnvironmentVariable("DOCKER_HOST")
        ?? (OperatingSystem.IsWindows()
            ? "npipe://./pipe/docker_engine"
            : "unix:///var/run/docker.sock");
}
