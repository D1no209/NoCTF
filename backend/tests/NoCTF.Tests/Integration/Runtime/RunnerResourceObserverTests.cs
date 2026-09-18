using k8s;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.Runtime.Docker.Containers;
using NoCTF.Runtime.Kubernetes.Configuration;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RunnerResourceObserverTests
{
    [Test, Timeout(300_000)]
    public async Task Host_observation_uses_explicit_mounts_and_rejects_a_second_domain_owner(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var root = Path.Combine(Path.GetTempPath(), "noctf-observation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "proc/sys/kernel"));
            Directory.CreateDirectory(Path.Combine(root, "cgroup"));
            try
            {
                await File.WriteAllTextAsync(Path.Combine(root, "machine-id"), "test-resource-domain", ct);
                await File.WriteAllTextAsync(Path.Combine(root, "proc/meminfo"), "MemTotal: 1048576 kB\nMemAvailable: 800000 kB\n", ct);
                await File.WriteAllTextAsync(Path.Combine(root, "proc/loadavg"), "0 0 0 1/20 1", ct);
                await File.WriteAllTextAsync(Path.Combine(root, "proc/sys/kernel/pid_max"), "10000", ct);
                await File.WriteAllTextAsync(Path.Combine(root, "proc/vmstat"), "oom_kill 0", ct);
                await File.WriteAllTextAsync(Path.Combine(root, "proc/stat"), "cpu 10 0 10 80 0 0 0 0\ncpu0 10 0 10 80 0 0 0 0", ct);
                var options = Options.Create(new RunnerOptions
                {
                    Id = "first", Pool = "test", Provider = RuntimeProvider.Libvirt,
                    Admission = new()
                    {
                        HostProcRoot = Path.Combine(root, "proc"), HostCgroupRoot = Path.Combine(root, "cgroup"),
                        HostIdentityPath = Path.Combine(root, "machine-id")
                    }
                });
                var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
                var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSql"] = postgres.GetConnectionString()
                }).Build();
                var lifetime = Substitute.For<IHostApplicationLifetime>();
                await using var first = new RunnerResourceObserver(options, new DockerRuntimeOptions(), new KubernetesRuntimeOptions(),
                    Substitute.For<IKubernetes>(), config, lifetime, clock, NullLogger<RunnerResourceObserver>.Instance);
                await Assert.That((await first.SampleAsync(ct)).State).IsEqualTo(RunnerAdmissionState.Starting);
                clock.Advance(TimeSpan.FromSeconds(5));
                await File.WriteAllTextAsync(Path.Combine(root, "proc/stat"), "cpu 15 0 15 170 0 0 0 0\ncpu0 15 0 15 170 0 0 0 0", ct);
                var ready = await first.SampleAsync(ct);
                await Assert.That(ready.State).IsEqualTo(RunnerAdmissionState.Ready);
                await Assert.That(ready.Observation!.MemoryTotalBytes).IsEqualTo(1073741824);
                await Assert.That(ready.Observation.PidsUsed).IsEqualTo(20);
                await using var duplicate = new RunnerResourceObserver(options, new DockerRuntimeOptions(), new KubernetesRuntimeOptions(),
                    Substitute.For<IKubernetes>(), config, lifetime, clock, NullLogger<RunnerResourceObserver>.Instance);
                await duplicate.SampleAsync(ct);
                clock.Advance(TimeSpan.FromSeconds(5));
                await File.WriteAllTextAsync(Path.Combine(root, "proc/stat"), "cpu 20 0 20 260 0 0 0 0\ncpu0 20 0 20 260 0 0 0 0", ct);
                await Assert.That((await duplicate.SampleAsync(ct)).State).IsEqualTo(RunnerAdmissionState.Starting);
                lifetime.Received(1).StopApplication();
            }
            finally { Directory.Delete(root, recursive: true); }
        });
    }
}
