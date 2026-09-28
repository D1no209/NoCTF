using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
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
    public async Task Wsrx_only_runtime_has_no_host_binding_and_gateway_uses_runtime_network(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var imageProbe = new ContainerBuilder("busybox:1.36.1")
                .WithCommand("true")
                .Build();
            await imageProbe.StartAsync(cancellationToken);
            await using var gateway = new ContainerBuilder("busybox:1.36.1")
                .WithCommand("sleep", "300")
                .WithLabel("noctf.io/runtime-proxy-gateway", "true")
                .Build();
            await gateway.StartAsync(cancellationToken);
            using var docker = new DockerClientBuilder()
                .WithEndpoint(new Uri(DockerEndpoint()))
                .Build();
            var operationId = Guid.NewGuid();
            var network = await docker.Networks.CreateNetworkAsync(
                new NetworksCreateParameters
                {
                    Name = $"noctf-wsrx-it-{operationId:N}",
                    Labels = new Dictionary<string, string>
                    {
                        ["noctf.io/managed"] = "true",
                        ["noctf.io/runtime-instance-id"] = operationId.ToString("D")
                    }
                },
                cancellationToken);
            using var lifecycle = new DockerContainerLifecycle(new DockerRuntimeOptions(
                Endpoint: DockerEndpoint(),
                ProxyContainerName: gateway.Id));
            ContainerReceipt? receipt = null;
            try
            {
                var request = new ContainerRequest(
                    operationId,
                    RuntimeProvider.Docker,
                    "busybox:1.36.1",
                    [
                        "/bin/sh",
                        "-c",
                        "mkdir -p /www && echo wsrx > /www/index.html && exec httpd -f -p 8080 -h /www"
                    ],
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>
                    {
                        ["noctf.io/managed"] = "true",
                        ["noctf.io/job-kind"] = "persistent-runtime",
                        ["noctf.io/runtime-instance-id"] = operationId.ToString("D")
                    },
                    new Dictionary<int, int>(),
                    new RuntimeResourceLimits(67_108_864, 100_000_000, 64),
                    new ContainerSecurityPolicy(true, false, false, ["ALL"], []),
                    TimeSpan.FromMinutes(5),
                    NetworkName: network.ID,
                    InternalPorts: [8080],
                    RuntimeInstanceId: operationId,
                    AccessMode: RuntimeAccessMode.WsrxOnly);
                receipt = await lifecycle.CreateAsync(request, cancellationToken);

                await Assert.That(receipt.PortMappings).IsEmpty();
                await Assert.That(receipt.InternalHost).IsNotNull().And.IsNotEmpty();
                var target = await docker.Containers.InspectContainerAsync(
                    receipt.ResourceId,
                    cancellationToken);
                await Assert.That(target.HostConfig?.PortBindings?.Values
                    .SelectMany(bindings => bindings)
                    .Any()).IsFalse();
                var attached = await docker.Networks.InspectNetworkAsync(
                    network.ID,
                    cancellationToken);
                await Assert.That(attached.Containers.Keys).Contains(gateway.Id);
                var probe = await gateway.ExecAsync(
                    [
                        "/bin/sh",
                        "-c",
                        $"wget -T 5 -q -O- http://{receipt.InternalHost}:8080 | grep -q wsrx"
                    ],
                    cancellationToken);
                await Assert.That(probe.ExitCode).IsEqualTo(0);

                await docker.Networks.DisconnectNetworkAsync(
                    network.ID,
                    new NetworkDisconnectParameters
                    {
                        Container = gateway.Id,
                        Force = true
                    },
                    cancellationToken);
                var replay = await lifecycle.EnsureRunningAsync(
                    request,
                    cancellationToken);
                await Assert.That(replay.ResourceId).IsEqualTo(receipt.ResourceId);
                await Assert.That(replay.InternalHost).IsEqualTo(receipt.InternalHost);
                attached = await docker.Networks.InspectNetworkAsync(
                    network.ID,
                    cancellationToken);
                await Assert.That(attached.Containers.Keys).Contains(gateway.Id);

                await lifecycle.DestroyAsync(
                    receipt with { NetworkId = network.ID },
                    cancellationToken);
                receipt = null;
                var detached = await docker.Networks.InspectNetworkAsync(
                    network.ID,
                    cancellationToken);
                await Assert.That(detached.Containers.Keys).DoesNotContain(gateway.Id);
            }
            finally
            {
                if (receipt is not null)
                {
                    await lifecycle.DestroyAsync(
                        receipt with { NetworkId = network.ID },
                        CancellationToken.None);
                }
                await docker.Networks.DeleteNetworkAsync(network.ID, CancellationToken.None);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Container_that_ignores_sigterm_is_force_removed_within_the_cleanup_budget(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var imageProbe = new ContainerBuilder("busybox:1.36.1")
                .WithCommand("true")
                .Build();
            await imageProbe.StartAsync(cancellationToken);
            using var lifecycle = CreateLifecycle();
            var operationId = Guid.NewGuid();
            var networkName = $"noctf-stop-it-{operationId:N}";
            using var docker = new DockerClientBuilder()
                .WithEndpoint(new Uri(DockerEndpoint()))
                .Build();
            var network = await docker.Networks.CreateNetworkAsync(
                new NetworksCreateParameters { Name = networkName },
                cancellationToken);
            ContainerReceipt? receipt = null;
            try
            {
                receipt = await lifecycle.CreateAsync(new ContainerRequest(
                    operationId,
                    RuntimeProvider.Docker,
                    "busybox:1.36.1",
                    ["/bin/sh", "-c", "trap '' TERM; while true; do sleep 1; done"],
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>
                    {
                        ["noctf.io/managed"] = "true",
                        ["noctf.io/runtime-instance-id"] = operationId.ToString("D")
                    },
                    new Dictionary<int, int>(),
                    new RuntimeResourceLimits(128 * 1024 * 1024, 100_000_000, 64),
                    new ContainerSecurityPolicy(true, false, false, ["ALL"], []),
                    TimeSpan.FromMinutes(1),
                    NetworkName: networkName,
                    RuntimeInstanceId: operationId), cancellationToken);
                var started = System.Diagnostics.Stopwatch.GetTimestamp();

                await lifecycle.DestroyAsync(
                    receipt,
                    RuntimeTerminationMode.GracefulThenForce,
                    new RuntimeTerminationPolicy(
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(8),
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(3)),
                    cancellationToken);

                await Assert.That(System.Diagnostics.Stopwatch.GetElapsedTime(started))
                    .IsLessThan(TimeSpan.FromSeconds(8));
                await Assert.That(await lifecycle.GetAsync(
                    RuntimeProvider.Docker,
                    receipt.ResourceId,
                    cancellationToken)).IsNull();
                receipt = null;
            }
            finally
            {
                if (receipt is not null)
                    await lifecycle.DestroyAsync(
                        receipt,
                        RuntimeTerminationMode.Force,
                        RuntimeTerminationPolicy.Default,
                        CancellationToken.None);
                await docker.Networks.DeleteNetworkAsync(network.ID, CancellationToken.None);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task One_shot_output_is_bounded_per_stream(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var dockerProbe = new ContainerBuilder("busybox:1.36.1")
                .WithCommand("true")
                .Build();
            await dockerProbe.StartAsync(cancellationToken);
            const int outputLimit = 128;
            using var lifecycle = new DockerContainerLifecycle(new DockerRuntimeOptions(
                Endpoint: DockerEndpoint(),
                OneShotOutputLimitBytesPerStream: outputLimit));
            var operationId = Guid.NewGuid();

            var result = await lifecycle.RunAsync(new ContainerRequest(
                operationId,
                RuntimeProvider.Docker,
                "busybox:1.36.1",
                ["/bin/sh", "-c", "head -c 4096 /dev/zero | tr '\\0' A; head -c 4096 /dev/zero | tr '\\0' B >&2"],
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<int, int>(),
                new RuntimeResourceLimits(128 * 1024 * 1024, 100_000_000, 64),
                new ContainerSecurityPolicy(true, false, false, ["ALL"], []),
                TimeSpan.FromMinutes(2),
                NetworkName: "none",
                NetworkPurpose: ContainerNetworkPurpose.AwdpVerification), null, cancellationToken);

            await Assert.That(result.ExitCode).IsEqualTo(0);
            await Assert.That(result.StandardOutput).IsEqualTo(new string('A', outputLimit));
            await Assert.That(result.StandardError).IsEqualTo(new string('B', outputLimit));
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Missing_local_image_is_pulled_before_container_creation(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            var operationId = Guid.NewGuid();
            const string image = "docker.m.daocloud.io/library/busybox:1.37.0-glibc";
            await using (var sourceProbe = new ContainerBuilder(image)
                .WithCommand("true")
                .Build())
            {
                await sourceProbe.StartAsync(cancellationToken);
            }
            using var docker = new DockerClientBuilder()
                .WithEndpoint(new Uri(DockerEndpoint()))
                .Build();
            _ = await docker.Images.DeleteImageAsync(
                image,
                new ImageDeleteParameters { Force = true },
                cancellationToken);
            Func<Task> inspectMissing = async () =>
                _ = await docker.Images.InspectImageAsync(image, cancellationToken);
            await Assert.That(inspectMissing).Throws<DockerImageNotFoundException>();

            using var lifecycle = CreateLifecycle(Path.Combine(
                Path.GetTempPath(),
                $"noctf-empty-docker-config-{operationId:N}"));
            ContainerReceipt? receipt = null;
            try
            {
                receipt = await lifecycle.CreateAsync(new(
                    operationId,
                    RuntimeProvider.Docker,
                    image,
                    ["sleep", "300"],
                    new Dictionary<string, string>(),
                    new Dictionary<string, string>(),
                    new Dictionary<int, int>(),
                    new RuntimeResourceLimits(128 * 1024 * 1024, 100_000_000, 64),
                    new ContainerSecurityPolicy(true, false, false, ["ALL"], []),
                    TimeSpan.FromMinutes(5),
                    NetworkName: "none",
                    NetworkPurpose: ContainerNetworkPurpose.AwdpVerification), cancellationToken);

                await Assert.That(receipt.Status).IsEqualTo(RuntimeStatus.Running);
                var inspect = await docker.Containers.InspectContainerAsync(
                    receipt.ResourceId,
                    cancellationToken);
                await Assert.That(inspect.HostConfig!.LogConfig.Type).IsEqualTo("local");
                await Assert.That(inspect.HostConfig.LogConfig.Config["max-size"])
                    .IsEqualTo("10485760");
                await Assert.That(inspect.HostConfig.LogConfig.Config["max-file"])
                    .IsEqualTo("3");
                _ = await docker.Images.InspectImageAsync(image, cancellationToken);
            }
            finally
            {
                if (receipt is not null)
                    await lifecycle.DestroyAsync(receipt, CancellationToken.None);
                try
                {
                    _ = await docker.Images.DeleteImageAsync(
                        image,
                        new ImageDeleteParameters { Force = true },
                        CancellationToken.None);
                }
                catch (DockerImageNotFoundException)
                {
                    // A failed pull leaves no local image to remove.
                }
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Run_as_non_root_rejects_a_root_image_before_container_creation(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            const string image = "busybox:1.36.1";
            await using var imageProbe = new ContainerBuilder(image)
                .WithCommand("true")
                .Build();
            await imageProbe.StartAsync(cancellationToken);
            using var docker = new DockerClientBuilder()
                .WithEndpoint(new Uri(DockerEndpoint()))
                .Build();
            var inspectedImage = await docker.Images.InspectImageAsync(image, cancellationToken);
            await Assert.That(inspectedImage.Config?.User ?? string.Empty).IsEmpty();
            var operationId = Guid.NewGuid();
            using var lifecycle = CreateLifecycle();

            var action = async () => await lifecycle.CreateAsync(new(
                operationId,
                RuntimeProvider.Docker,
                image,
                ["sleep", "300"],
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<int, int>(),
                new RuntimeResourceLimits(128 * 1024 * 1024, 100_000_000, 64),
                new ContainerSecurityPolicy(true, false, true, ["ALL"], []),
                TimeSpan.FromMinutes(1),
                NetworkName: "none",
                NetworkPurpose: ContainerNetworkPurpose.AwdpVerification), cancellationToken);

            await Assert.That(action).Throws<RuntimeConfigurationException>();
            Func<Task> inspectContainer = async () =>
                _ = await docker.Containers.InspectContainerAsync(
                    $"noctf-{operationId:N}",
                    cancellationToken);
            await Assert.That(inspectContainer).Throws<DockerContainerNotFoundException>();
        });
    }

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
                cancellationToken);
            try
            {
                var replay = await lifecycle.CreateIsolatedNetworkAsync(
                    SandboxRequest(operationId),
                    cancellationToken);

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
    public async Task Concurrent_isolated_network_replays_share_one_live_network(
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

            var networkIds = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => lifecycle.CreateIsolatedNetworkAsync(
                    SandboxRequest(operationId),
                    cancellationToken)));
            var networkId = networkIds[0];
            try
            {
                await Assert.That(networkIds.Distinct().ToArray())
                    .IsEquivalentTo([networkId]);
                await Assert.That(await lifecycle.IsolatedNetworkExistsAsync(
                    networkId,
                    cancellationToken)).IsTrue();
            }
            finally
            {
                await lifecycle.DeleteIsolatedNetworkAsync(networkId, CancellationToken.None);
                await lifecycle.DeleteIsolatedNetworkAsync(networkId, CancellationToken.None);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Persistent_runtime_publishes_the_target_port_directly(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var targetImage = new ContainerBuilder("busybox:1.36.1")
                .WithCommand("true")
                .Build();
            await targetImage.StartAsync(cancellationToken);
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
                "127.0.0.1");
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
                        ["noctf.io/runtime-instance-id"] = operationId.ToString("D")
                    },
                    new Dictionary<int, int> { [8080] = 0 },
                    new RuntimeResourceLimits(128 * 1024 * 1024, 100_000_000, 64),
                    new ContainerSecurityPolicy(true, false, false, ["ALL"], []),
                    TimeSpan.FromMinutes(5),
                    NetworkIsolation: ContainerNetworkIsolation.Isolated,
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

                await Assert.That(receipt.InternalHost)
                    .IsEqualTo("target");
                await Assert.That(receipt.PortMappings[8080]).IsGreaterThan(0);
                await Assert.That(replay.ResourceId).IsEqualTo(receipt.ResourceId);
                await Assert.That(replay.PortMappings[8080])
                    .IsEqualTo(receipt.PortMappings[8080]);
                using var http = new HttpClient();
                var response = await GetEventuallyAsync(
                    http,
                    $"http://127.0.0.1:{receipt.PortMappings[8080]}",
                    cancellationToken);
                await Assert.That(response.Trim()).IsEqualTo("target");
                var target = await docker.Containers.InspectContainerAsync(
                    receipt.ResourceId,
                    cancellationToken);
                var runtimeNetwork = await docker.Networks.InspectNetworkAsync(
                    receipt.NetworkId!,
                    cancellationToken);
                await Assert.That(runtimeNetwork.Internal).IsFalse();
                await Assert.That(runtimeNetwork.Labels.ContainsKey("noctf.io/expires-at"))
                    .IsFalse();
                await Assert.That(target.NetworkSettings!.Networks.Keys)
                    .DoesNotContain(platformNetworkName);

                await Assert.That(await reconciler.ListManagedAsync(cancellationToken))
                    .Contains(new RuntimeResourceIdentity(operationId));
                var targetId = receipt.ResourceId;
                var networkId = receipt.NetworkId!;
                await reconciler.DestroyByIdentityAsync(
                    new(operationId),
                    cancellationToken);
                receipt = null;
                await Assert.That(await lifecycle.GetAsync(
                    RuntimeProvider.Docker,
                    targetId,
                    cancellationToken)).IsNull();
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
    public async Task Receipt_cleanup_removes_the_exact_container_and_network(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var dockerProbe = new ContainerBuilder("alpine:3.20")
                .WithCommand("true")
                .Build();
            await dockerProbe.StartAsync(cancellationToken);
            using var lifecycle = CreateLifecycle();
            var runtimeId = Guid.NewGuid();
            var identity = new RuntimeResourceIdentity(runtimeId);
            ContainerReceipt? receipt = null;

            try
            {
                receipt = await IsolatedContainerProvisioner.ProvisionAsync(
                    lifecycle,
                    lifecycle,
                    CheckerRequest(runtimeId, null) with
                    {
                        AllowInternalCallback = false,
                        RuntimeInstanceId = runtimeId,
                        NetworkIsolation = ContainerNetworkIsolation.Isolated,
                        InternalPorts = [8080]
                    },
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                await Assert.That(receipt.RuntimeInstanceId).IsEqualTo(runtimeId);
                await Assert.That(receipt.NetworkId).IsNotNull();

                await RuntimeReceiptCleanup.CleanupContainerAsync(
                    new DockerOnlyProviderCatalog(lifecycle),
                    identity,
                    RuntimeProvider.Docker,
                    ContainerRuntimeReceiptData.From(receipt),
                    cancellationToken);

                var remaining = await lifecycle.GetAsync(
                    RuntimeProvider.Docker,
                    receipt.ResourceId,
                    cancellationToken);
                await Assert.That(remaining).IsNull();
                await Assert.That(await lifecycle.IsolatedNetworkExistsAsync(
                    receipt.NetworkId!,
                    cancellationToken)).IsFalse();
            }
            finally
            {
                if (receipt is not null)
                {
                    if (await lifecycle.GetAsync(
                            RuntimeProvider.Docker,
                            receipt.ResourceId,
                            CancellationToken.None) is not null)
                    {
                        await lifecycle.DestroyAsync(receipt, CancellationToken.None);
                    }
                    if (receipt.NetworkId is { Length: > 0 } remainingNetworkId
                        && await lifecycle.IsolatedNetworkExistsAsync(
                            remainingNetworkId,
                            CancellationToken.None))
                    {
                        await lifecycle.DeleteIsolatedNetworkAsync(
                            remainingNetworkId,
                            CancellationToken.None);
                    }
                }
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Reconciler_tracks_and_removes_awdp_verification_resources(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var image = new ContainerBuilder("busybox:1.36.1")
                .WithCommand("true")
                .Build();
            await image.StartAsync(cancellationToken);
            var runtimeId = Guid.NewGuid();
            var networkName = $"noctf-awdp-reconcile-{runtimeId:N}";
            var containerName = $"noctf-awdp-reconcile-target-{runtimeId:N}";
            using var docker = new DockerClientBuilder()
                .WithEndpoint(new Uri(DockerEndpoint()))
                .Build();
            var labels = new Dictionary<string, string>
            {
                ["noctf.io/managed"] = "true",
                ["noctf.io/job-kind"] = "awdp-verification",
                ["noctf.io/runtime-instance-id"] = runtimeId.ToString("D")
            };
            var network = await docker.Networks.CreateNetworkAsync(
                new NetworksCreateParameters
                {
                    Name = networkName,
                    Labels = labels
                },
                cancellationToken);
            var created = await docker.Containers.CreateContainerAsync(
                new CreateContainerParameters
                {
                    Name = containerName,
                    Image = "busybox:1.36.1",
                    Cmd = ["/bin/sh", "-c", "sleep 300"],
                    Labels = labels,
                    HostConfig = new HostConfig { NetworkMode = networkName }
                },
                cancellationToken);
            _ = await docker.Containers.StartContainerAsync(
                created.ID,
                new ContainerStartParameters(),
                cancellationToken);
            var options = new DockerRuntimeOptions(DockerEndpoint());
            var composeWorkRoot = Path.Combine(
                Path.GetTempPath(),
                $"noctf-awdp-compose-reconcile-{runtimeId:N}");
            using var reconciler = new DockerRuntimeResourceReconciler(
                options,
                new DockerComposeRuntime(options, workDirectory: composeWorkRoot));
            try
            {
                var identity = new RuntimeResourceIdentity(runtimeId);
                await Assert.That(await reconciler.ListManagedAsync(cancellationToken))
                    .Contains(identity);

                await reconciler.DestroyByIdentityAsync(identity, cancellationToken);

                await Assert.That(await reconciler.ListManagedAsync(cancellationToken))
                    .DoesNotContain(identity);
                Func<Task> inspectContainer = async () =>
                    _ = await docker.Containers.InspectContainerAsync(
                        created.ID,
                        cancellationToken);
                Func<Task> inspectNetwork = async () =>
                    _ = await docker.Networks.InspectNetworkAsync(
                        network.ID,
                        cancellationToken);
                await Assert.That(inspectContainer).ThrowsException();
                await Assert.That(inspectNetwork).ThrowsException();
            }
            finally
            {
                try
                {
                    await docker.Containers.RemoveContainerAsync(
                        created.ID,
                        new ContainerRemoveParameters { Force = true },
                        CancellationToken.None);
                }
                catch (DockerContainerNotFoundException)
                {
                    // The reconciler already removed the exact test resource.
                }
                try
                {
                    await docker.Networks.DeleteNetworkAsync(
                        network.ID,
                        CancellationToken.None);
                }
                catch (DockerApiException exception) when (
                    exception.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // The reconciler already removed the exact test resource.
                }
                if (Directory.Exists(composeWorkRoot))
                    Directory.Delete(composeWorkRoot, recursive: true);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_persistent_runtimes_keep_their_allocated_ports(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var targetImage = new ContainerBuilder("busybox:1.36.1")
                .WithCommand("true")
                .Build();
            await targetImage.StartAsync(cancellationToken);
            var operationIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
            var platformNetworkName = $"noctf-concurrent-it-{Guid.NewGuid():N}";
            using var docker = new DockerClientBuilder()
                .WithEndpoint(new Uri(DockerEndpoint()))
                .Build();
            var platformNetwork = await docker.Networks.CreateNetworkAsync(
                new NetworksCreateParameters { Name = platformNetworkName },
                cancellationToken);
            using var lifecycle = new DockerContainerLifecycle(new DockerRuntimeOptions(
                DockerEndpoint(),
                platformNetworkName,
                "127.0.0.1"));
            var receipts = new List<ContainerReceipt>();
            try
            {
                var provisionTasks = operationIds.Select(operationId =>
                    lifecycle.CreateAsync(
                        new ContainerRequest(
                            operationId,
                            RuntimeProvider.Docker,
                            "busybox:1.36.1",
                            ["/bin/sh", "-c", "mkdir -p /www && echo target > /www/index.html && exec httpd -f -p 8080 -h /www"],
                            new Dictionary<string, string>(),
                            new Dictionary<string, string>(),
                            new Dictionary<int, int> { [8080] = 0 },
                            new RuntimeResourceLimits(128 * 1024 * 1024, 100_000_000, 64),
                            new ContainerSecurityPolicy(true, false, false, ["ALL"], []),
                            TimeSpan.FromMinutes(5),
                            NetworkName: platformNetworkName,
                            RuntimeInstanceId: operationId),
                        cancellationToken));

                receipts.AddRange(await Task.WhenAll(provisionTasks));
                using var http = new HttpClient();
                for (var index = 0; index < receipts.Count; index++)
                {
                    var receipt = receipts[index];
                    await Assert.That(receipt.PortMappings[8080]).IsGreaterThan(0);
                    var expanded = RuntimeUrlExpander.ExpandContainer(
                        receipt,
                        [new RuntimeUrlBinding(
                            "http://{HOST}:{PORT}/",
                            RuntimeExposure.OwnerOnly,
                            ContainerPort: 8080)]);
                    await Assert.That(expanded.DirectAddresses.Single())
                        .IsEqualTo($"http://127.0.0.1:{receipt.PortMappings[8080]}/");
                    var response = await GetEventuallyAsync(
                        http,
                        expanded.DirectAddresses.Single(),
                        cancellationToken);
                    await Assert.That(response.Trim()).IsEqualTo("target");
                }
                await Assert.That(receipts.Select(item => item.PortMappings[8080]).Distinct())
                    .Count().IsEqualTo(receipts.Count);
            }
            finally
            {
                foreach (var receipt in receipts)
                    await lifecycle.DestroyAsync(receipt, CancellationToken.None);
                await docker.Networks.DeleteNetworkAsync(
                    platformNetwork.ID,
                    CancellationToken.None);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Ensure_running_rejects_stale_identity_or_port_contract(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var targetImage = new ContainerBuilder("busybox:1.36.1")
                .WithCommand("true")
                .Build();
            await targetImage.StartAsync(cancellationToken);
            var operationId = Guid.NewGuid();
            var platformNetworkName = $"noctf-reconcile-it-{operationId:N}";
            using var docker = new DockerClientBuilder()
                .WithEndpoint(new Uri(DockerEndpoint()))
                .Build();
            var platformNetwork = await docker.Networks.CreateNetworkAsync(
                new NetworksCreateParameters { Name = platformNetworkName },
                cancellationToken);
            using var lifecycle = new DockerContainerLifecycle(new DockerRuntimeOptions(
                DockerEndpoint(),
                platformNetworkName,
                "localhost"));
            var labels = new Dictionary<string, string>
            {
                ["noctf.io/managed"] = "true",
                ["noctf.io/job-kind"] = "persistent-runtime",
                ["noctf.io/runtime-instance-id"] = operationId.ToString("D")
            };
            var request = new ContainerRequest(
                operationId,
                RuntimeProvider.Docker,
                "busybox:1.36.1",
                ["sleep", "300"],
                new Dictionary<string, string>(),
                labels,
                new Dictionary<int, int> { [8080] = 0 },
                new RuntimeResourceLimits(128 * 1024 * 1024, 100_000_000, 64),
                new ContainerSecurityPolicy(true, false, false, ["ALL"], []),
                TimeSpan.FromMinutes(5),
                NetworkName: platformNetworkName,
                RuntimeInstanceId: operationId);
            ContainerReceipt? receipt = null;
            try
            {
                receipt = await lifecycle.CreateAsync(request, cancellationToken);

                Func<Task> changedPort = async () =>
                    _ = await lifecycle.EnsureRunningAsync(
                        request with
                        {
                            PortMappings = new Dictionary<int, int>
                            {
                                [8080] = 61000
                            }
                        },
                        cancellationToken);
                await Assert.That(changedPort).Throws<ArgumentOutOfRangeException>();

                var existing = await docker.Containers.InspectContainerAsync(
                    receipt.ResourceId,
                    cancellationToken);
                IReadOnlyList<string> actualHostPorts = [];
                if (existing.NetworkSettings?.Ports is { } actualBindings
                    && actualBindings.TryGetValue("8080/tcp", out var portBindings))
                    actualHostPorts = [.. portBindings.Select(binding => binding.HostPort).Distinct()];
                await Assert.That(actualHostPorts).Contains(
                    receipt.PortMappings[8080].ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
            }
            finally
            {
                if (receipt is not null)
                    await lifecycle.DestroyAsync(receipt, CancellationToken.None);
                await docker.Networks.DeleteNetworkAsync(
                    platformNetwork.ID,
                    CancellationToken.None);
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
            var callbackRole = $"test-callback-{Guid.NewGuid():N}";
            await using var callback = new ContainerBuilder("alpine:3.20")
                .WithCommand("sleep", "300")
                .WithLabel("noctf.io/internal-role", callbackRole)
                .Build();
            await callback.StartAsync(cancellationToken);
            var endpoint = DockerEndpoint();
            using var docker = new DockerClientBuilder().WithEndpoint(new Uri(endpoint)).Build();
            using var lifecycle = new DockerContainerLifecycle(new DockerRuntimeOptions(
                endpoint,
                "noctf-platform",
                "localhost",
                CallbackContainerLabelValue: callbackRole));
            var firstOperationId = Guid.NewGuid();
            var secondOperationId = Guid.NewGuid();
            var firstSandbox = await lifecycle.CreateIsolatedNetworkAsync(
                SandboxRequest(firstOperationId),
                cancellationToken);
            var secondSandbox = await lifecycle.CreateIsolatedNetworkAsync(
                SandboxRequest(secondOperationId),
                cancellationToken);
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

            Func<Task> inspectFirstCallback = async () =>
                _ = await docker.Networks.InspectNetworkAsync(
                    $"noctf-callback-{firstOperationId:N}",
                    cancellationToken);
            Func<Task> inspectSecondCallback = async () =>
                _ = await docker.Networks.InspectNetworkAsync(
                    $"noctf-callback-{secondOperationId:N}",
                    cancellationToken);
            await Assert.That(inspectFirstCallback).ThrowsException();
            await Assert.That(inspectSecondCallback).ThrowsException();
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
                .WithLabel("noctf.io/internal-role", "scoring-callback-gateway")
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
                    ["noctf.io/runtime-instance-id"] = runtimeId.ToString("D")
                },
                new Dictionary<int, int>(),
                new RuntimeResourceLimits(128 * 1024 * 1024, 100_000_000, 64),
                new ContainerSecurityPolicy(true, false, false, ["ALL"], []),
                TimeSpan.FromMinutes(5),
                NetworkIsolation: ContainerNetworkIsolation.Isolated,
                InternalPorts: [8080],
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
                        ["noctf.io/purpose"] = "awd-checker"
                    },
                    new Dictionary<int, int>(),
                    new RuntimeResourceLimits(128 * 1024 * 1024, 100_000_000, 64),
                    new ContainerSecurityPolicy(true, false, false, ["ALL"], []),
                    TimeSpan.FromMinutes(1),
                    OperationTimeout: TimeSpan.FromSeconds(30),
                    AllowInternalCallback: true,
                    RuntimeInstanceId: runtimeId,
                    NetworkPurpose: ContainerNetworkPurpose.AwdChecker);

                var result = await lifecycle.RunAttachedAsync(
                    checkerRequest,
                    new AttachedContainerRuntimeTarget(
                        new RuntimeResourceIdentity(runtimeId),
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
                    cancellationToken)).Internal).IsFalse();
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
                cancellationToken);
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

    private static DockerContainerLifecycle CreateLifecycle(
        string? registryConfigDirectory = null) => new(new DockerRuntimeOptions(
        DockerEndpoint(),
        RegistryConfigDirectory: registryConfigDirectory));

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
            $"Docker runtime did not become reachable at '{url}'.",
            lastFailure);
    }

    private static ContainerNetworkPolicyRequest SandboxRequest(Guid operationId) =>
        new(
            new RuntimeResourceIdentity(operationId),
            ContainerNetworkPurpose.AwdpVerification,
            RuntimeEgressPolicy.Isolated,
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
        new ContainerSecurityPolicy(true, false, false, ["ALL"], []),
        TimeSpan.FromMinutes(1),
        NetworkName: networkName,
        AllowInternalCallback: true,
        NetworkPurpose: ContainerNetworkPurpose.AwdpVerification);

    private static ContainerReceipt Receipt(string resourceId) => new(
        Guid.NewGuid(), RuntimeProvider.Docker, resourceId, RuntimeStatus.Running,
        new Dictionary<int, int>(), "localhost", null);

    private sealed class DockerOnlyProviderCatalog(DockerContainerLifecycle lifecycle)
        : IRuntimeProviderCatalog
    {
        public IContainerLifecycle Containers(RuntimeProvider provider) =>
            provider == RuntimeProvider.Docker
                ? lifecycle
                : throw new UnsupportedRuntimeProviderException(provider);

        public IContainerSandboxLifecycle Sandbox(RuntimeProvider provider) =>
            provider == RuntimeProvider.Docker
                ? lifecycle
                : throw new UnsupportedRuntimeProviderException(provider);

        public IComposeRuntime Compose(RuntimeProvider provider) =>
            throw new UnsupportedRuntimeProviderException(provider);

        public IOvaRuntime Appliance(RuntimeProvider provider) =>
            throw new UnsupportedRuntimeProviderException(provider);
    }

}
