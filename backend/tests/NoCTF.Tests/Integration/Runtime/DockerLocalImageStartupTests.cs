using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker.Containers;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class DockerLocalImageStartupTests
{
    [Test, Timeout(300_000)]
    public async Task Installed_local_alias_starts_and_cleans_up_without_a_remote_repository(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var dependency = new ContainerBuilder("busybox:1.36.1").WithCommand("true").Build();
            await dependency.StartAsync(ct);
            var endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST")
                ?? (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock");
            using var client = new DockerClientBuilder().WithEndpoint(new Uri(endpoint)).Build();
            var localImage = $"noctf-local-test-{Guid.NewGuid():N}";
            await client.Images.TagImageAsync("busybox:1.36.1", new ImageTagParameters
            {
                RepositoryName = localImage,
                Tag = "latest"
            }, ct);
            using var lifecycle = new DockerContainerLifecycle(new(Endpoint: endpoint, NetworkName: "none"));
            ContainerReceipt? receipt = null;
            try
            {
                var id = Guid.NewGuid();
                receipt = await lifecycle.CreateAsync(new ContainerRequest(id, RuntimeProvider.Docker,
                    localImage, ["sleep", "60"], new Dictionary<string, string>(),
                    new Dictionary<string, string>(), new Dictionary<int, int>(),
                    new(64 * 1024 * 1024, 200, 64), null, RuntimeInstanceId: id), ct);
                var actual = await client.Containers.InspectContainerAsync(receipt.ResourceId, ct);
                var image = await client.Images.InspectImageAsync(localImage, ct);
                await Assert.That(actual.Image).IsEqualTo(image.ID);
                await Assert.That(actual.State?.Running ?? false).IsTrue();
                await lifecycle.DestroyAsync(receipt, ct);
                await Assert.That(async () => await client.Containers.InspectContainerAsync(receipt.ResourceId, ct))
                    .Throws<DockerContainerNotFoundException>();
                receipt = null;
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                if (receipt is not null) await lifecycle.DestroyAsync(receipt, cleanup.Token);
                await client.Images.DeleteImageAsync(localImage + ":latest", new ImageDeleteParameters(), cleanup.Token);
            }
        });
    }
}
