using DotNet.Testcontainers.Builders;
using Docker.DotNet;
using Docker.DotNet.Models;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
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
            var runtime = new DockerComposeRuntime(
                new DockerRuntimeOptions(
                    PublicHost: "localhost",
                    NetworkName: platformNetworkName),
                workDirectory: workRoot);
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
                    $"http://localhost:{web.PublishedPorts[8080]}",
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
                var composeContainers = await docker.Containers.ListContainersAsync(
                    new ContainersListParameters
                    {
                        All = true,
                        Filters = new Dictionary<string, IDictionary<string, bool>>
                        {
                            ["label"] = new Dictionary<string, bool>
                            {
                                [$"com.docker.compose.project={receipt.ProjectName}"] = true
                            }
                        }
                    },
                    cancellationToken);
                var ingress = composeContainers.Single(container =>
                    container.Labels.TryGetValue(
                        "com.docker.compose.service",
                        out var service)
                    && service == DockerComposeIngressProxyPolicy.ProxyServiceName);
                var ingressInspect = await docker.Containers.InspectContainerAsync(
                    ingress.ID,
                    cancellationToken);
                await Assert.That(ingressInspect.NetworkSettings!.Networks.Keys)
                    .Contains(platformNetworkName);
                var webInspect = await docker.Containers.InspectContainerAsync(
                    web.ResourceId,
                    cancellationToken);
                await Assert.That(webInspect.NetworkSettings!.Networks.Keys)
                    .DoesNotContain(platformNetworkName);

                var resourceIds = status.Services
                    .Select(service => service.ResourceId)
                    .ToArray();
                var operationDirectory = receipt.Namespace;
                await runtime.DownAsync(receipt, cancellationToken);
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
            $"Docker Compose ingress proxy did not become reachable at '{url}'.",
            lastFailure);
    }

    private static ComposeRequest Request(Guid operationId) => new(
        operationId,
        RuntimeProvider.Docker,
        1,
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
            ["noctf.io/runtime-instance-id"] = operationId.ToString("D"),
            ["noctf.io/generation"] = "1"
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
        ]);
}
