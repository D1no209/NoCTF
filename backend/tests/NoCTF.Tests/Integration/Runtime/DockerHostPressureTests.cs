using System.Diagnostics;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using k8s;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.Runtime.Docker.Containers;
using NoCTF.Runtime.Kubernetes.Configuration;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class DockerHostPressureTests
{
    [Test, Timeout(180_000)]
    public async Task Actual_daemon_host_pressure_closes_admission_and_recovers(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("NOCTF_HOST_PRESSURE_TEST") != "true")
            Skip.Test("Requires an explicitly enabled Linux daemon-host stress fixture with read-only host mounts.");
        await using var postgres = new PostgreSqlBuilder(
            "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
        await postgres.StartAsync(ct);
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PostgreSql"] = postgres.GetConnectionString()
        }).Build();
        await using var observer = new RunnerResourceObserver(
            Options.Create(new RunnerOptions { Id = "host-pressure-test", Provider = RuntimeProvider.Docker }),
            new DockerRuntimeOptions(Endpoint: "unix:///var/run/docker.sock"), new KubernetesRuntimeOptions(),
            Substitute.For<IKubernetes>(), configuration, lifetime, TimeProvider.System, NullLogger<RunnerResourceObserver>.Instance);
        var observations = new List<object>();
        async Task<RunnerAdmissionState> SampleAsync()
        {
            var watch = Stopwatch.StartNew();
            var sample = await observer.SampleAsync(ct);
            observations.Add(new { sample, collectionMs = watch.Elapsed.TotalMilliseconds });
            Console.WriteLine(JsonSerializer.Serialize(observations[^1]));
            return sample.State;
        }
        await SampleAsync();
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        await Assert.That(await SampleAsync()).IsEqualTo(RunnerAdmissionState.Ready);
        // A bounded stress process consumes the daemon VM's CPUs; automatic container disposal stops it.
        await using var stress = new ContainerBuilder("busybox:1.36.1")
            .WithCommand("sh", "-c", "i=0; while [ $i -lt 64 ]; do (while :; do :; done) & i=$((i+1)); done; wait")
            .WithLabel("noctf.test", "host-pressure").Build();
        await stress.StartAsync(ct);
        var blocked = false;
        for (var sample = 0; sample < 8 && !blocked; sample++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            blocked = await SampleAsync() == RunnerAdmissionState.PressureBlocked;
        }
        await stress.StopAsync(ct);
        await Assert.That(blocked).IsTrue();
        var recovered = false;
        for (var sample = 0; sample < 5 && !recovered; sample++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            recovered = await SampleAsync() == RunnerAdmissionState.Ready;
        }
        await Assert.That(recovered).IsTrue();
        lifetime.DidNotReceive().StopApplication();
        var output = Environment.GetEnvironmentVariable("NOCTF_CAPACITY_MEASUREMENTS");
        if (!string.IsNullOrWhiteSpace(output))
            await File.WriteAllTextAsync(Path.Combine(output, "docker-host-pressure.json"),
                JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }), ct);
    }
}
