using Docker.DotNet;
using Docker.DotNet.Models;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using DotNet.Testcontainers.Builders;
using k8s;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Challenges.Capabilities;
using NoCTF.Runner.Composition;
using NoCTF.Runtime.Docker.Containers;
using NoCTF.Runtime.Docker.Services;
using NoCTF.Runtime.Kubernetes.Configuration;
using NoCTF.Runtime.Kubernetes.Containers;
using NoCTF.Runtime.Kubernetes.Services;
using Microsoft.Extensions.Options;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class CheckCapabilityDockerTests
{
    private const string Image = "docker.m.daocloud.io/library/alpine@sha256:2e0b81d60597f449baa56dc25d355367869d2dca4cd57e866f1e2b52af3eb55f";

    [Test, Arguments(CheckExecutionUse.Verification), Arguments(CheckExecutionUse.Observation), Timeout(300_000)]
    public async Task Common_check_executes_real_workloads_and_keeps_callback_credentials_out_of_the_target(CheckExecutionUse use, CancellationToken ct)
    {
        var endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST")
            ?? (OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock");
        using var client = new DockerClientBuilder().WithEndpoint(new Uri(endpoint)).Build();
        var name = $"capability-check-{Guid.NewGuid():N}";
        var targetNetwork = await client.Networks.CreateNetworkAsync(new NetworksCreateParameters { Name = name, Driver = "bridge", Internal = true }, ct);
        var callbackNetwork = await client.Networks.CreateNetworkAsync(new NetworksCreateParameters { Name = name + "-callback", Driver = "bridge", Internal = true }, ct);
        var callback = new ContainerBuilder(Image).WithLabel("noctf.io/internal-role", "scoring-callback-gateway")
            .WithCommand("sleep", "3600").Build();
        var options = new DockerRuntimeOptions(Endpoint: endpoint, NetworkName: name, CallbackNetworkName: name + "-callback",
            RegistryConfigDirectory: Path.Combine(AppContext.BaseDirectory, "Fixtures", "Docker"));
        using var containers = new DockerContainerLifecycle(options);
        var directory = Path.Combine(AppContext.BaseDirectory, "check-input-tests", Guid.NewGuid().ToString("N"));
        ContainerReceipt? target = null;
        try
        {
            await callback.StartAsync(ct);
            var runtimeId = Guid.NewGuid();
            target = await containers.CreateAsync(new(runtimeId, RuntimeProvider.Docker, Image, ["sleep", "3600"],
                new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<int, int>(),
                new(128 * 1024 * 1024, 250, 64), TimeSpan.FromMinutes(5), RuntimeInstanceId: runtimeId), ct);
            using var kubernetes = new Kubernetes(new KubernetesClientConfiguration { Host = "http://127.0.0.1:1" });
            var kubernetesOptions = new KubernetesRuntimeOptions();
            var kubernetesContainers = new KubernetesContainerLifecycle(kubernetes, kubernetesOptions);
            var catalog = new RuntimeProviderCatalog(containers, kubernetesContainers, new DockerContainerRuntime(containers, options),
                new KubernetesContainerRuntime(kubernetes, kubernetesContainers, kubernetesOptions), []);
            var execute = new ExecuteCheck(new RuntimeCheckExecutor(catalog, new RuntimeExecutionOptions()));
            var factId = Guid.NewGuid();
            CheckExecutionInput Input(IReadOnlyList<string> command, TimeSpan duration) => new(runtimeId, RuntimeProvider.Docker, Image,
                command, new Dictionary<string, string>(), target, target.InternalHost!, 1, new Uri("http://callback/check-results"),
                "capability-check-credential", duration, ScopeId: factId)
                { Requirements = [CheckItemKind.Functionality], Use = use, ExecutionId = Guid.NewGuid() };
            var success = await execute.ExecuteAsync(Input(["/bin/sh", "-c",
                "test \"$NOCTF_CALLBACK_TOKEN\" = capability-check-credential && test \"$NOCTF_CHECK_REQUIREMENTS\" = '[\"Functionality\"]'"], TimeSpan.FromSeconds(20)), null, ct);
            await Assert.That(success.Succeeded).IsTrue();
            await Assert.That(success.Value!.ScopeId).IsEqualTo(factId);
            Directory.CreateDirectory(directory);
            foreach (var format in new[] { TarEntryFormat.Pax, TarEntryFormat.Gnu })
            {
                using var upload = new MemoryStream();
                using (var gzip = new GZipStream(upload, CompressionLevel.Fastest, leaveOpen: true))
                using (var tar = new TarWriter(gzip, format, leaveOpen: true))
                using (var script = new MemoryStream(Encoding.UTF8.GetBytes("printf applied > /target-only-state\n")))
                    tar.WriteEntry(format == TarEntryFormat.Pax ? new PaxTarEntry(TarEntryType.RegularFile, "fix.sh") { DataStream = script }
                        : new GnuTarEntry(TarEntryType.RegularFile, "fix.sh") { DataStream = script });
                upload.Position = 0;
                var prepared = Path.Combine(directory, $"{format}.tar");
                await new FixArchivePreparer(Options.Create(new FixVerificationOptions())).PrepareTarAsync(upload, "fix.tar.gz", "fix.sh",
                    Path.Combine(directory, format.ToString()), prepared, ct);
                await using (var patch = File.OpenRead(prepared)) await containers.CopyArchiveAsync(target, patch, ct);
                var applied = await containers.ExecAsync(target, ["/bin/sh", "/noctf/fix/fix.sh"], TimeSpan.FromSeconds(5), ct);
                await Assert.That(applied.ExitCode).IsEqualTo(0);
                await using var checkerInput = File.OpenRead(prepared);
                var archive = new OneShotInputArchive(checkerInput, OneShotInputArchive.RootDestinationPath);
                var command = Input(["/bin/sh", "-c", "test -f /noctf/fix/fix.sh && test ! -e /target-only-state && test \"$NOCTF_CALLBACK_TOKEN\" = capability-check-credential"], TimeSpan.FromSeconds(20));
                var denied = await execute.ExecuteAsync(command, archive, ct);
                await Assert.That(denied.FailureCode).IsEqualTo(CapabilityExecutionFailure.InvalidInput);
                await Assert.That(checkerInput.Position).IsEqualTo(0L);
                var checkedInput = await execute.ExecuteAsync(command with { FixInputEnabled = true }, archive, ct);
                await Assert.That(checkedInput.Succeeded).IsTrue();
                await Assert.That(archive.PreparationCompleted).IsTrue();
            }
            var failed = await execute.ExecuteAsync(Input(["/bin/sh", "-c", "exit 7"], TimeSpan.FromSeconds(20)), null, ct);
            await Assert.That(failed.FailureCode).IsEqualTo(CapabilityExecutionFailure.TargetExecutionFailed);
            var timedOut = await execute.ExecuteAsync(Input(["sleep", "30"], TimeSpan.FromSeconds(3)), null, ct);
            await Assert.That(timedOut.FailureCode).IsEqualTo(CapabilityExecutionFailure.DeadlineExceeded);
            await Assert.That((await containers.GetAsync(RuntimeProvider.Docker, target.ResourceId, ct))!.Status).IsEqualTo(RuntimeStatus.Running);
            var environment = await containers.ExecAsync(target, ["/bin/sh", "-c", "test -z \"${NOCTF_CALLBACK_TOKEN+x}\" && test -z \"${NOCTF_CALLBACK_URL+x}\""], TimeSpan.FromSeconds(5), ct);
            await Assert.That(environment.ExitCode).IsEqualTo(0);
        }
        finally
        {
            if (target is not null) await containers.DestroyAsync(target, ct);
            await callback.DisposeAsync();
            await client.Networks.DeleteNetworkAsync(callbackNetwork.ID, ct);
            await client.Networks.DeleteNetworkAsync(targetNetwork.ID, ct);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
