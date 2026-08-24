using DotNet.Testcontainers.Builders;
using Docker.DotNet;
using Docker.DotNet.Models;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker;
using NoCTF.Runtime.Docker.Compose;
using NoCTF.Runtime.Docker.Containers;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
[NotInParallel]
public sealed class DockerComposeRuntimeIntegrationTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Real_Docker_Compose_provisions_resolves_ports_executes_and_cleans_up(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var dockerProbe = new ContainerBuilder("busybox:1.36.1")
                .WithCommand("true")
                .Build();
            await dockerProbe.StartAsync(cancellationToken);
            await using var callback = new ContainerBuilder("busybox:1.36.1")
                .WithCommand(
                    "/bin/sh",
                    "-c",
                    "mkdir -p /www && echo callback > /www/index.html && exec httpd -f -p 8080 -h /www")
                .WithLabel("noctf.io/internal-role", "scoring-callback-gateway")
                .Build();
            await callback.StartAsync(cancellationToken);
            var operationId = Guid.NewGuid();
            var platformNetworkName = $"noctf-platform-it-{operationId:N}";
            using var docker = new DockerClientBuilder()
                .WithEndpoint(new Uri(DockerEndpoint()))
                .Build();
            var platformNetwork = await docker.Networks.CreateNetworkAsync(
                new NetworksCreateParameters
                {
                    Name = platformNetworkName
                },
                cancellationToken);
            var workRoot = Path.Combine(
                Path.GetTempPath(),
                $"noctf-compose-it-{operationId:N}");
            var runtimeOptions = new DockerRuntimeOptions(
                Endpoint: DockerEndpoint(),
                PublicHost: "127.0.0.1",
                NetworkName: platformNetworkName,
                CallbackContainerName: callback.Id);
            var runtime = new DockerComposeRuntime(
                runtimeOptions,
                workDirectory: workRoot);
            using var containerLifecycle = new DockerContainerLifecycle(runtimeOptions);
            using var reconciler = new DockerRuntimeResourceReconciler(
                runtimeOptions,
                runtime);
            ComposeReceipt? receipt = null;
            try
            {
                receipt = await runtime.UpAsync(
                    Request(operationId),
                    cancellationToken);
                var status = await runtime.GetStatusAsync(receipt, cancellationToken);

                await Assert.That(status).IsNotNull();
                await Assert.That(status!.Status).IsEqualTo(RuntimeStatus.Running);
                await Assert.That(status.Services).Count().IsEqualTo(2);
                var web = status.Services.Single(service => service.Name == "web");
                await Assert.That(web.PublishedPorts.ContainsKey(8080)).IsTrue();
                await Assert.That(web.PublishedPorts[8080]).IsGreaterThan(0);
                using var http = new HttpClient();
                var publicResponse = await GetEventuallyAsync(
                    http,
                    $"http://127.0.0.1:{web.PublishedPorts[8080]}",
                    cancellationToken);
                await Assert.That(publicResponse.Trim()).IsEqualTo("web");
                var sameRuntime = await runtime.ExecAsync(
                    receipt,
                    "web",
                    [
                        "/bin/sh",
                        "-c",
                        "wget -T 5 -q -O- http://db:9090 | grep -q db"
                    ],
                    TimeSpan.FromSeconds(10),
                    cancellationToken);
                await Assert.That(sameRuntime)
                    .IsEqualTo(new ContainerExecResult(0, false));
                var injectedFlag = await runtime.ExecAsync(
                    receipt,
                    "web",
                    ["/bin/sh", "-c", "test \"$FLAG\" = 'flag{compose-runtime}'"],
                    TimeSpan.FromSeconds(10),
                    cancellationToken);
                var uninjectedFlag = await runtime.ExecAsync(
                    receipt,
                    "db",
                    ["/bin/sh", "-c", "test -z \"${FLAG+x}\""],
                    TimeSpan.FromSeconds(10),
                    cancellationToken);
                await Assert.That(injectedFlag)
                    .IsEqualTo(new ContainerExecResult(0, false));
                await Assert.That(uninjectedFlag)
                    .IsEqualTo(new ContainerExecResult(0, false));
                var webInspect = await docker.Containers.InspectContainerAsync(
                    web.ResourceId,
                    cancellationToken);
                await Assert.That(webInspect.NetworkSettings!.Networks.Keys)
                    .DoesNotContain(platformNetworkName);
                await Assert.That(webInspect.HostConfig!.Memory).IsEqualTo(67_108_864);
                await Assert.That(webInspect.HostConfig.LogConfig.Type).IsEqualTo("local");
                await Assert.That(webInspect.HostConfig.LogConfig.Config["max-size"])
                    .IsEqualTo("10485760");
                await Assert.That(webInspect.HostConfig.LogConfig.Config["max-file"])
                    .IsEqualTo("3");
                await Assert.That(webInspect.HostConfig.NanoCPUs).IsEqualTo(100_000_000);
                await Assert.That(webInspect.HostConfig.PidsLimit).IsEqualTo(64);
                await Assert.That(webInspect.HostConfig.Privileged).IsFalse();
                await Assert.That(webInspect.HostConfig.CapDrop).Contains("ALL");
                await Assert.That(webInspect.HostConfig.SecurityOpt)
                    .Contains("no-new-privileges:true");

                var checkerOperationId = Guid.NewGuid();
                var checkerResult = await containerLifecycle.RunAttachedAsync(
                    new ContainerRequest(
                        checkerOperationId,
                        RuntimeProvider.Docker,
                        "busybox:1.36.1",
                        [
                            "/bin/sh",
                            "-c",
                            "sleep 1 && wget -qO- \"$NOCTF_TARGET_URL\" | grep -q db && "
                            + "wget -qO- \"$NOCTF_CALLBACK_URL\" | grep -q callback"
                        ],
                        new Dictionary<string, string>
                        {
                            ["NOCTF_TARGET_URL"] = "http://db:9090",
                            ["NOCTF_CALLBACK_URL"] = "http://callback:8080/"
                        },
                        new Dictionary<string, string>
                        {
                            ["noctf.io/managed"] = "true",
                            ["noctf.io/job-kind"] = "awd-checker",
                            ["noctf.io/runtime-instance-id"] = operationId.ToString("D"),
                            ["noctf.io/purpose"] = "awd-checker"
                        },
                        new Dictionary<int, int>(),
                        new RuntimeResourceLimits(128 * 1024 * 1024, 100_000_000, 64),
                        new ContainerSecurityPolicy(true, false, false, ["ALL"], []),
                        TimeSpan.FromMinutes(1),
                        OperationTimeout: TimeSpan.FromSeconds(30),
                        AllowInternalCallback: true,
                        RuntimeInstanceId: operationId,
                        NetworkPurpose: ContainerNetworkPurpose.AwdChecker),
                    new AttachedComposeRuntimeTarget(
                        new RuntimeResourceIdentity(operationId),
                        receipt,
                        "db"),
                    cancellationToken);
                await Assert.That(checkerResult.StandardError).IsEmpty();
                await Assert.That(checkerResult.ExitCode).IsEqualTo(0);
                await Assert.That((await runtime.GetStatusAsync(receipt, cancellationToken))!.Status)
                    .IsEqualTo(RuntimeStatus.Running);
                Func<Task> inspectCheckerCallback = async () =>
                    _ = await docker.Networks.InspectNetworkAsync(
                        $"noctf-callback-{checkerOperationId:N}",
                        cancellationToken);
                await Assert.That(inspectCheckerCallback).ThrowsException();

                var resourceIds = status.Services
                    .Select(service => service.ResourceId)
                    .ToArray();
                var operationDirectory = receipt.Namespace;
                await Assert.That(await reconciler.ListManagedAsync(cancellationToken))
                    .Contains(new RuntimeResourceIdentity(operationId));
                await reconciler.DestroyByIdentityAsync(
                    new(operationId),
                    cancellationToken);
                receipt = null;

                foreach (var resourceId in resourceIds)
                {
                    Func<Task> inspect = async () =>
                        _ = await docker.Containers.InspectContainerAsync(
                            resourceId,
                            cancellationToken);
                    await Assert.That(inspect).ThrowsException();
                }
                Func<Task> inspectNetwork = async () =>
                    _ = await docker.Networks.InspectNetworkAsync(
                        $"it-{operationId:N}_default",
                        cancellationToken);
                await Assert.That(inspectNetwork).ThrowsException();
                await Assert.That(Directory.Exists(operationDirectory)).IsFalse();
            }
            finally
            {
                if (receipt is not null)
                    await runtime.DownAsync(receipt, CancellationToken.None);
                if (Directory.Exists(workRoot))
                    Directory.Delete(workRoot, recursive: true);
                await docker.Networks.DeleteNetworkAsync(
                    platformNetwork.ID,
                    CancellationToken.None);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Failed_up_removes_partial_resources_and_work_directory(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var targetImage = new ContainerBuilder("busybox:1.36.1")
                .WithCommand("true")
                .Build();
            await targetImage.StartAsync(cancellationToken);
            var operationId = Guid.NewGuid();
            var request = Request(operationId) with
            {
                ComposeYaml = Request(operationId).ComposeYaml.Replace(
                    "exec httpd",
                    "false && httpd",
                    StringComparison.Ordinal)
            };
            var workRoot = Path.Combine(
                Path.GetTempPath(),
                $"noctf-compose-failed-{operationId:N}");
            var runtime = new DockerComposeRuntime(
                new DockerRuntimeOptions(
                    Endpoint: DockerEndpoint()),
                workDirectory: workRoot);
            using var docker = new DockerClientBuilder()
                .WithEndpoint(new Uri(DockerEndpoint()))
                .Build();
            Func<Task> action = async () =>
                _ = await runtime.UpAsync(request, cancellationToken);

            try
            {
                await Assert.That(action).Throws<ComposeCommandFailedException>();
                var containers = await docker.Containers.ListContainersAsync(
                    new ContainersListParameters
                    {
                        All = true,
                        Filters = new Dictionary<string, IDictionary<string, bool>>
                        {
                            ["label"] = new Dictionary<string, bool>
                            {
                                [$"com.docker.compose.project={request.ProjectName}"] = true
                            }
                        }
                    },
                    cancellationToken);
                await Assert.That(containers).IsEmpty();
                Func<Task> inspectNetwork = async () =>
                    _ = await docker.Networks.InspectNetworkAsync(
                        $"{request.ProjectName}_default",
                        cancellationToken);
                await Assert.That(inspectNetwork).ThrowsException();
                await Assert.That(Directory.Exists(
                    Path.Combine(workRoot, operationId.ToString("N")))).IsFalse();
            }
            finally
            {
                if (Directory.Exists(workRoot))
                    Directory.Delete(workRoot, recursive: true);
            }
        });
    }

    [Test]
    public async Task Invalid_definition_does_not_leave_an_operation_work_directory()
    {
        var operationId = Guid.NewGuid();
        var workRoot = Path.Combine(
            Path.GetTempPath(),
            $"noctf-compose-invalid-{operationId:N}");
        var runtime = new DockerComposeRuntime(
            new DockerRuntimeOptions(Endpoint: DockerEndpoint()),
            workDirectory: workRoot);
        Func<Task> action = async () =>
            _ = await runtime.UpAsync(
                Request(operationId) with
                {
                    EgressPolicy = RuntimeEgressPolicy.InternetOnly
                },
                CancellationToken.None);

        try
        {
            await Assert.That(action).Throws<InvalidOperationException>();
            await Assert.That(Directory.Exists(
                Path.Combine(workRoot, operationId.ToString("N")))).IsFalse();
        }
        finally
        {
            if (Directory.Exists(workRoot))
                Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    [Timeout(300_000)]
    public async Task Replay_preserves_the_service_and_down_removes_all_resources(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var dockerProbe = new ContainerBuilder("busybox:1.36.1")
                .WithCommand("true")
                .Build();
            await dockerProbe.StartAsync(cancellationToken);
            var operationId = Guid.NewGuid();
            var request = PrivateRequest(operationId);
            var workRoot = Path.Combine(
                Path.GetTempPath(),
                $"noctf-compose-down-{operationId:N}");
            var runtime = new DockerComposeRuntime(
                new DockerRuntimeOptions(Endpoint: DockerEndpoint()),
                workDirectory: workRoot);
            using var docker = new DockerClientBuilder()
                .WithEndpoint(new Uri(DockerEndpoint()))
                .Build();
            ComposeReceipt? receipt = null;
            string? resourceId = null;
            try
            {
                receipt = await runtime.UpAsync(request, cancellationToken);
                var first = await runtime.GetStatusAsync(receipt, cancellationToken);
                resourceId = first!.Services.Single().ResourceId;

                var replay = await runtime.UpAsync(request, cancellationToken);
                var replayed = await runtime.GetStatusAsync(replay, cancellationToken);

                await Assert.That(replay.Namespace).IsEqualTo(receipt.Namespace);
                await Assert.That(replayed!.Services.Single().ResourceId)
                    .IsEqualTo(resourceId);
                var flag = await runtime.ExecAsync(
                    replay,
                    "worker",
                    ["/bin/sh", "-c", "test \"$FLAG\" = 'flag{compose-replay}'"],
                    TimeSpan.FromSeconds(10),
                    cancellationToken);
                await Assert.That(flag).IsEqualTo(new ContainerExecResult(0, false));

                await runtime.DownAsync(receipt, cancellationToken);
                receipt = null;

                Func<Task> inspectContainer = async () =>
                    _ = await docker.Containers.InspectContainerAsync(
                        resourceId,
                        cancellationToken);
                Func<Task> inspectNetwork = async () =>
                    _ = await docker.Networks.InspectNetworkAsync(
                        $"{request.ProjectName}_default",
                        cancellationToken);
                await Assert.That(inspectContainer).ThrowsException();
                await Assert.That(inspectNetwork).ThrowsException();
                await Assert.That(Directory.Exists(
                    Path.Combine(workRoot, operationId.ToString("N")))).IsFalse();
            }
            finally
            {
                if (receipt is not null)
                    await runtime.DownAsync(receipt, CancellationToken.None);
                if (Directory.Exists(workRoot))
                    Directory.Delete(workRoot, recursive: true);
            }
        });
    }

    private static string DockerEndpoint() =>
        Environment.GetEnvironmentVariable("DOCKER_HOST")
        ?? (OperatingSystem.IsWindows()
            ? "npipe://./pipe/docker_engine"
            : "unix:///var/run/docker.sock");

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
            $"Docker Compose runtime did not become reachable at '{url}'.",
            lastFailure);
    }

    private static ComposeRequest Request(Guid operationId) => new(
        operationId,
        RuntimeProvider.Docker,
        $"it-{operationId:N}",
        """
        services:
          web:
            image: busybox:1.36.1
            command:
              - /bin/sh
              - -c
              - mkdir -p /www && echo web > /www/index.html && exec httpd -f -p 8080 -h /www
          db:
            image: busybox:1.36.1
            command:
              - /bin/sh
              - -c
              - mkdir -p /www && echo db > /www/index.html && exec httpd -f -p 9090 -h /www
        """,
        new Dictionary<string, string>(),
        new Dictionary<string, string>
        {
            ["noctf.io/managed"] = "true",
            ["noctf.io/job-kind"] = "persistent-runtime",
            ["noctf.io/runtime-instance-id"] = operationId.ToString("D")
        },
        new Dictionary<string, RuntimeResourceLimits>
        {
            ["web"] = new(67_108_864, 100_000_000, 64),
            ["db"] = new(67_108_864, 100_000_000, 64)
        },
        new(134_217_728, 200_000_000, 128),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(2),
        [
            new RuntimeUrlBinding(
                "http://{HOST}:{PORT}",
                RuntimeExposure.Participants,
                ContainerPort: 8080,
                ServiceName: "web")
        ],
        ServiceEnvironment:
            new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["web"] = new Dictionary<string, string>
                {
                    ["FLAG"] = "flag{compose-runtime}"
                }
            },
        PublishedPorts:
        [
            new("web", 8080, 0)
        ]);

    private static ComposeRequest PrivateRequest(Guid operationId) => new(
        operationId,
        RuntimeProvider.Docker,
        $"down-{operationId:N}",
        """
        services:
          worker:
            image: busybox:1.36.1
            command:
              - sleep
              - "300"
        """,
        new Dictionary<string, string>(),
        new Dictionary<string, string>
        {
            ["noctf.io/managed"] = "true",
            ["noctf.io/job-kind"] = "persistent-runtime",
            ["noctf.io/runtime-instance-id"] = operationId.ToString("D")
        },
        new Dictionary<string, RuntimeResourceLimits>
        {
            ["worker"] = new(67_108_864, 100_000_000, 64)
        },
        new(67_108_864, 100_000_000, 64),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(2),
        ServiceEnvironment:
            new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["worker"] = new Dictionary<string, string>
                {
                    ["FLAG"] = "flag{compose-replay}"
                }
            });

}
