using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Docker;
using NoCTF.Runtime.Docker.Containers;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class DockerAnonymousVolumeCleanupTests
{
    [Test, Arguments(false, false, false), Arguments(true, false, false), Arguments(false, true, false), Arguments(false, false, true), Timeout(300_000)]
    public Task Destroy_removes_anonymous_volumes_and_preserves_named_volumes(bool byIdentity, bool named, bool oneShot, CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        var directory = Path.Combine(Environment.CurrentDirectory, ".codex", "volume-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "Dockerfile"), "FROM busybox:1.36.1\nVOLUME /ephemeral\nCMD [\"sleep\",\"120\"]\n", ct);
        await using var image = new ImageFromDockerfileBuilder().WithDockerfileDirectory(directory).WithName("noctf-volume-test:" + Guid.NewGuid().ToString("N")).Build();
        await image.CreateAsync(ct);
        var endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST") ?? (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock");
        using var docker = new DockerClientBuilder().WithEndpoint(new Uri(endpoint)).Build();
        var options = new DockerRuntimeOptions(Endpoint: endpoint, NetworkName: "none");
        using var lifecycle = new DockerContainerLifecycle(options);
        using var reconciler = new DockerRuntimeResourceReconciler(options);
        var id = Guid.NewGuid();
        var request = new ContainerRequest(id, RuntimeProvider.Docker, image.FullName, ["sleep", "120"], new Dictionary<string, string>(),
            new Dictionary<string, string> { ["noctf.io/managed"] = "true", ["noctf.io/runtime-instance-id"] = id.ToString("D") },
            new Dictionary<int, int>(), new(64 * 1024 * 1024, 200, 64), null, NetworkName: "none", RuntimeInstanceId: id);
        var namedVolume = "noctf-volume-control-" + id.ToString("N");
        Task<OneShotResult>? job = null;
        ContainerReceipt receipt;
        if (named)
        {
            await docker.Volumes.CreateAsync(new() { Name = namedVolume }, ct);
            var created = await docker.Containers.CreateContainerAsync(new()
            {
                Image = image.FullName, Cmd = ["sleep", "120"],
                HostConfig = new() { NetworkMode = "none", Binds = [namedVolume + ":/ephemeral"] }
            }, ct);
            receipt = new(id, RuntimeProvider.Docker, created.ID, RuntimeStatus.Pending, new Dictionary<int, int>(), null, null);
        }
        else if (oneShot)
        {
            job = lifecycle.RunAsync(request with { Command = ["sleep", "4"] }, null, ct);
            ContainerInspectResponse? running = null;
            for (var attempt = 0; attempt < 50 && running is null; attempt++)
            {
                try { running = await docker.Containers.InspectContainerAsync("noctf-" + id.ToString("N"), ct); }
                catch (DockerContainerNotFoundException) { await Task.Delay(100, ct); }
            }
            if (running is null) throw new InvalidOperationException("One-shot container was not created.");
            receipt = new(id, RuntimeProvider.Docker, running.ID, RuntimeStatus.Running, new Dictionary<int, int>(), null, null);
        }
        else receipt = await lifecycle.CreateAsync(request, ct);
        var container = await docker.Containers.InspectContainerAsync(receipt.ResourceId, ct);
        var volume = container.Mounts.Single(mount => mount.Destination == "/ephemeral").Name
            ?? throw new InvalidOperationException("Docker volume name missing.");
        try
        {
            await Assert.That((await docker.Volumes.InspectAsync(volume, ct)).Name).IsEqualTo(volume);
            if (job is not null) await Assert.That((await job).ExitCode).IsEqualTo(0);
            else if (byIdentity) await reconciler.DestroyByIdentityAsync(new(id), ct);
            else await lifecycle.DestroyAsync(receipt, ct);
            if (named) await Assert.That((await docker.Volumes.InspectAsync(volume, ct)).Name).IsEqualTo(namedVolume);
            else
            {
                var missing = await Assert.That(async () => await docker.Volumes.InspectAsync(volume, ct)).Throws<DockerApiException>();
                await Assert.That(missing!.StatusCode).IsEqualTo(System.Net.HttpStatusCode.NotFound);
            }
            await Assert.That(async () => await docker.Containers.InspectContainerAsync(receipt.ResourceId, ct)).Throws<DockerContainerNotFoundException>();
        }
        finally
        {
            try { await docker.Containers.RemoveContainerAsync(receipt.ResourceId, new() { Force = true, RemoveVolumes = true }, CancellationToken.None); }
            catch (DockerContainerNotFoundException) { }
            try { await docker.Volumes.RemoveAsync(volume, false, CancellationToken.None); }
            catch (DockerApiException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound) { }
            File.Delete(Path.Combine(directory, "Dockerfile"));
        }
    });
}
