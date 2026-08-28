using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NSubstitute;

namespace NoCTF.Tests.Unit.Runner;

public sealed class RunnerProviderReadinessDependencyTests
{
    [Test]
    public async Task Checks_only_the_configured_runtime_provider()
    {
        var docker = Substitute.For<IRuntimeProviderAvailabilityProbe>();
        docker.Provider.Returns(RuntimeProvider.Docker);
        var kubernetes = Substitute.For<IRuntimeProviderAvailabilityProbe>();
        kubernetes.Provider.Returns(RuntimeProvider.Kubernetes);
        var health = CreateHealth();
        var dependency = new RunnerProviderReadinessDependency(
            Options.Create(new RunnerOptions
            {
                Provider = RuntimeProvider.Kubernetes
            }),
            new RuntimeProviderAvailabilityCatalog([docker, kubernetes]),
            health);

        await dependency.CheckAsync(CancellationToken.None);

        await kubernetes.Received(1).CheckAvailabilityAsync(CancellationToken.None);
        await docker.DidNotReceive().CheckAvailabilityAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Missing_configured_provider_probe_fails_readiness()
    {
        var health = CreateHealth();
        var dependency = new RunnerProviderReadinessDependency(
            Options.Create(new RunnerOptions
            {
                Provider = RuntimeProvider.Libvirt
            }),
            new RuntimeProviderAvailabilityCatalog([]),
            health);

        var action = () => dependency.CheckAsync(CancellationToken.None);

        await Assert.That(action).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Recent_provider_resource_failure_blocks_readiness_until_recovery_or_cooldown()
    {
        var docker = Substitute.For<IRuntimeProviderAvailabilityProbe>();
        docker.Provider.Returns(RuntimeProvider.Docker);
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-19T00:00:00Z"));
        var health = CreateHealth(clock, holdSeconds: 60);
        var dependency = new RunnerProviderReadinessDependency(
            Options.Create(new RunnerOptions
            {
                Provider = RuntimeProvider.Docker
            }),
            new RuntimeProviderAvailabilityCatalog([docker]),
            health);
        var runtimeId = Guid.CreateVersion7();

        health.ReportFailure(
            RuntimeProvider.Docker,
            RunnerProviderFailureKind.ProvisionRejected,
            runtimeId);
        var blocked = () => dependency.CheckAsync(CancellationToken.None);
        await Assert.That(blocked).Throws<InvalidOperationException>();
        await docker.Received(1).CheckAvailabilityAsync(CancellationToken.None);

        health.ReportSuccess(RuntimeProvider.Docker);
        await dependency.CheckAsync(CancellationToken.None);

        health.ReportFailure(
            RuntimeProvider.Docker,
            RunnerProviderFailureKind.ProvisionRejected,
            runtimeId);
        clock.Advance(TimeSpan.FromSeconds(61));
        await dependency.CheckAsync(CancellationToken.None);
    }

    private static RunnerProviderHealthState CreateHealth(
        TimeProvider? timeProvider = null,
        int holdSeconds = 120) => new(
            Options.Create(new RunnerOptions
            {
                ProviderFailureHoldSeconds = holdSeconds
            }),
            timeProvider ?? TimeProvider.System,
            NullLogger<RunnerProviderHealthState>.Instance);
}
