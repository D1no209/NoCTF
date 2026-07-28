using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Messages;
using NoCTF.Runtime.Docker;
using NoCTF.Runtime.Docker.Compose;
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
                SandboxRequest(operationId),
                DateTimeOffset.UtcNow.AddMinutes(1), cancellationToken);
            try
            {
                var replay = await lifecycle.CreateIsolatedNetworkAsync(
                    SandboxRequest(operationId),
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
    public async Task Persistent_runtime_uses_a_dual_network_ingress_proxy(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var targetImage = new ContainerBuilder("busybox:1.36.1")
                .WithCommand("true")
                .Build();
            await targetImage.StartAsync(cancellationToken);
            await using var proxyImage = new ContainerBuilder("haproxy:3.1-alpine")
                .WithCommand("haproxy", "-v")
                .Build();
            await proxyImage.StartAsync(cancellationToken);
            var operationId = Guid.NewGuid();
            var platformNetworkName = $"noctf-platform-it-{operationId:N}";
            using var docker = new DockerClientBuilder()
                .WithEndpoint(new Uri(DockerEndpoint()))
                .Build();
            var platformNetwork = await docker.Networks.CreateNetworkAsync(
                new NetworksCreateParameters { Name = platformNetworkName },
                cancellationToken);
            var options = new DockerRuntimeOptions(
                DockerEndpoint(),
                platformNetworkName,
                "localhost");
            using var lifecycle = new DockerContainerLifecycle(options);
            var composeWorkRoot = Path.Combine(
                Path.GetTempPath(),
                $"noctf-compose-reconcile-{operationId:N}");
            using var reconciler = new DockerRuntimeResourceReconciler(
                options,
                new DockerComposeRuntime(options, workDirectory: composeWorkRoot));
            ContainerReceipt? receipt = null;
            try
            {
                var request = new ContainerRequest(
                    operationId,
                    RuntimeProvider.Docker,
                    "busybox:1.36.1",
                    ["/bin/sh", "-c", "mkdir -p /www && echo target > /www/index.html && exec httpd -f -p 8080 -h /www"],
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>
                    {
                        ["noctf.io/managed"] = "true",
                        ["noctf.io/job-kind"] = "persistent-runtime",
                        ["noctf.io/runtime-instance-id"] = operationId.ToString("D"),
                        ["noctf.io/generation"] = "1"
                    },
                    new Dictionary<int, int> { [8080] = 0 },
                    new RuntimeResourceLimits(128 * 1024 * 1024, 100_000_000, 64),
                    new ContainerSecurityPolicy(true, false, false, ["ALL"], []),
                    TimeSpan.FromMinutes(5),
                    NetworkIsolation: ContainerNetworkIsolation.Isolated,
                    Generation: 1,
                    RuntimeInstanceId: operationId);

                receipt = await IsolatedContainerProvisioner.ProvisionAsync(
                    lifecycle,
                    lifecycle,
                    request,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                var replay = await IsolatedContainerProvisioner.ProvisionAsync(
                    lifecycle,
                    lifecycle,
                    request,
                    DateTimeOffset.UtcNow,
                    cancellationToken);

                await Assert.That(receipt.IngressResourceId).IsNotNull();
                await Assert.That(receipt.InternalHost)
                    .IsEqualTo($"noctf-ingress-{operationId:N}");
                await Assert.That(receipt.PortMappings[8080]).IsGreaterThan(0);
                await Assert.That(replay.ResourceId).IsEqualTo(receipt.ResourceId);
                await Assert.That(replay.IngressResourceId)
                    .IsEqualTo(receipt.IngressResourceId);
                await Assert.That(replay.PortMappings[8080])
                    .IsEqualTo(receipt.PortMappings[8080]);
                using var http = new HttpClient();
                var response = await GetEventuallyAsync(
                    http,
                    $"http://localhost:{receipt.PortMappings[8080]}",
                    cancellationToken);
                await Assert.That(response.Trim()).IsEqualTo("target");
                var target = await docker.Containers.InspectContainerAsync(
                    receipt.ResourceId,
                    cancellationToken);
                var ingress = await docker.Containers.InspectContainerAsync(
                    receipt.IngressResourceId!,
                    cancellationToken);
                var runtimeNetwork = await docker.Networks.InspectNetworkAsync(
                    receipt.NetworkId!,
                    cancellationToken);
                await Assert.That(runtimeNetwork.Internal).IsTrue();
                await Assert.That(runtimeNetwork.Labels.ContainsKey("noctf.io/expires-at"))
                    .IsFalse();
                await Assert.That(target.NetworkSettings!.Networks.Keys)
                    .DoesNotContain(platformNetworkName);
                await Assert.That(ingress.NetworkSettings!.Networks.Keys)
                    .Contains(platformNetworkName);
                await Assert.That(ingress.NetworkSettings.Networks).Count().IsEqualTo(2);

                await Assert.That(await reconciler.ListManagedAsync(cancellationToken))
                    .Contains(new RuntimeResourceIdentity(operationId, 1));
                var targetId = receipt.ResourceId;
                var ingressId = receipt.IngressResourceId!;
                var networkId = receipt.NetworkId!;
                await reconciler.DestroyByIdentityAsync(
                    new(operationId, 1),
                    cancellationToken);
                receipt = null;
                await Assert.That(await lifecycle.GetAsync(
                    RuntimeProvider.Docker,
                    targetId,
                    cancellationToken)).IsNull();
                Func<Task> inspectIngress = async () =>
                    _ = await docker.Containers.InspectContainerAsync(
                        ingressId,
                        cancellationToken);
                await Assert.That(inspectIngress).ThrowsException();
                Func<Task> inspectSandbox = async () =>
                    _ = await docker.Networks.InspectNetworkAsync(
                        networkId,
                        cancellationToken);
                await Assert.That(inspectSandbox).ThrowsException();
            }
            finally
            {
                if (receipt is not null)
                {
                    await IsolatedContainerProvisioner.DestroyAsync(
                        lifecycle,
                        lifecycle,
                        receipt,
                        CancellationToken.None);
                }
                await docker.Networks.DeleteNetworkAsync(
                    platformNetwork.ID,
                    CancellationToken.None);
                if (Directory.Exists(composeWorkRoot))
                    Directory.Delete(composeWorkRoot, recursive: true);
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
                SandboxRequest(firstOperationId),
                DateTimeOffset.UtcNow.AddMinutes(1), cancellationToken);
            var secondSandbox = await lifecycle.CreateIsolatedNetworkAsync(
                SandboxRequest(secondOperationId),
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
    public async Task Awd_checker_attaches_to_the_runtime_and_callback_networks(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var imageProbe = new ContainerBuilder("busybox:1.36.1")
                .WithCommand("true")
                .Build();
            await imageProbe.StartAsync(cancellationToken);
            await using var callback = new ContainerBuilder("busybox:1.36.1")
                .WithCommand(
                    "/bin/sh",
                    "-c",
                    "mkdir -p /www && echo callback > /www/index.html && exec httpd -f -p 8080 -h /www")
                .WithLabel("noctf.io/internal-role", "awdp-callback-gateway")
                .Build();
            await callback.StartAsync(cancellationToken);
            var runtimeId = Guid.NewGuid();
            var checkerOperationId = Guid.NewGuid();
            var endpoint = DockerEndpoint();
            using var docker = new DockerClientBuilder()
                .WithEndpoint(new Uri(endpoint))
                .Build();
            using var lifecycle = new DockerContainerLifecycle(new DockerRuntimeOptions(
                endpoint,
                "noctf-platform",
                "localhost",
                callback.Id));
            var targetRequest = new ContainerRequest(
                runtimeId,
                RuntimeProvider.Docker,
                "busybox:1.36.1",
                [
                    "/bin/sh",
                    "-c",
                    "mkdir -p /www && echo target > /www/index.html && exec httpd -f -p 8080 -h /www"
                ],
                new Dictionary<string, string>(),
                new Dictionary<string, string>
                {
                    ["noctf.io/managed"] = "true",
                    ["noctf.io/job-kind"] = "persistent-runtime",
                    ["noctf.io/runtime-instance-id"] = runtimeId.ToString("D"),
                    ["noctf.io/generation"] = "1"
                },
                new Dictionary<int, int>(),
                new RuntimeResourceLimits(128 * 1024 * 1024, 100_000_000, 64),
                new ContainerSecurityPolicy(true, false, false, ["ALL"], []),
                TimeSpan.FromMinutes(5),
                NetworkIsolation: ContainerNetworkIsolation.Isolated,
                InternalPorts: [8080],
                Generation: 1,
                RuntimeInstanceId: runtimeId);
            ContainerReceipt? targetReceipt = null;
            try
            {
                targetReceipt = await IsolatedContainerProvisioner.ProvisionAsync(
                    lifecycle,
                    lifecycle,
                    targetRequest,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                var checkerRequest = new ContainerRequest(
                    checkerOperationId,
                    RuntimeProvider.Docker,
                    "busybox:1.36.1",
                    [
                        "/bin/sh",
                        "-c",
                        "sleep 1 && wget -qO- \"$NOCTF_TARGET_URL\" | grep -q target && "
                        + "wget -qO- \"$NOCTF_CALLBACK_URL\" | grep -q callback"
                    ],
                    new Dictionary<string, string>
                    {
                        ["NOCTF_TARGET_URL"] = $"http://{targetReceipt.InternalHost}:8080",
                        ["NOCTF_CALLBACK_URL"] = "http://callback:8080/"
                    },
                    new Dictionary<string, string>
                    {
                        ["noctf.io/managed"] = "true",
                        ["noctf.io/job-kind"] = "awd-checker",
                        ["noctf.io/runtime-instance-id"] = runtimeId.ToString("D"),
                        ["noctf.io/generation"] = "1",
                        ["noctf.io/purpose"] = "awd-checker"
                    },
                    new Dictionary<int, int>(),
                    new RuntimeResourceLimits(128 * 1024 * 1024, 100_000_000, 64),
                    new ContainerSecurityPolicy(true, false, false, ["ALL"], []),
                    TimeSpan.FromMinutes(1),
                    OperationTimeout: TimeSpan.FromSeconds(30),
                    AllowInternalCallback: true,
                    Generation: 1,
                    RuntimeInstanceId: runtimeId,
                    NetworkPurpose: ContainerNetworkPurpose.AwdChecker);

                var result = await lifecycle.RunAttachedAsync(
                    checkerRequest,
                    new AttachedContainerRuntimeTarget(
                        new RuntimeResourceIdentity(runtimeId, 1),
                        targetReceipt),
                    cancellationToken);

                await Assert.That(result.StandardError).IsEmpty();
                await Assert.That(result.ExitCode).IsEqualTo(0);
                await Assert.That(await lifecycle.GetAsync(
                    RuntimeProvider.Docker,
                    targetReceipt.ResourceId,
                    cancellationToken)).IsNotNull();
                await Assert.That((await docker.Networks.InspectNetworkAsync(
                    targetReceipt.NetworkId!,
                    cancellationToken)).Internal).IsTrue();
                Func<Task> inspectCallback = async () =>
                    _ = await docker.Networks.InspectNetworkAsync(
                        $"noctf-callback-{checkerOperationId:N}",
                        cancellationToken);
                await Assert.That(inspectCallback).ThrowsException();
            }
            finally
            {
                if (targetReceipt is not null)
                {
                    await IsolatedContainerProvisioner.DestroyAsync(
                        lifecycle,
                        lifecycle,
                        targetReceipt,
                        CancellationToken.None);
                }
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
                SandboxRequest(operationId),
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

    private static async Task<string> GetEventuallyAsync(
        HttpClient client,
        string url,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                return await client.GetStringAsync(url, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                lastFailure = exception;
                await Task.Delay(250, cancellationToken);
            }
        }
        throw new InvalidOperationException(
            $"Docker ingress proxy did not become reachable at '{url}'.",
            lastFailure);
    }

    private static ContainerNetworkPolicyRequest SandboxRequest(Guid operationId) =>
        new(
            new RuntimeResourceIdentity(operationId, 1),
            ContainerNetworkPurpose.AwdpVerification,
            RuntimeEgressPolicy.DenyAll,
            [],
            8080);

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
        Generation: 1,
        NetworkPurpose: ContainerNetworkPurpose.AwdpVerification);

    private static ContainerReceipt Receipt(string resourceId) => new(
        Guid.NewGuid(), RuntimeProvider.Docker, resourceId, RuntimeStatus.Running,
        new Dictionary<int, int>(), "localhost", null);
}
