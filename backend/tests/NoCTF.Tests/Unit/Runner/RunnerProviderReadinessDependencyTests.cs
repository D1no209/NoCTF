using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        var services = new ServiceCollection();
        services.AddKeyedSingleton(RuntimeProvider.Docker, docker);
        services.AddKeyedSingleton(RuntimeProvider.Kubernetes, kubernetes);
        await using var provider = services.BuildServiceProvider();
        var health = CreateHealth();
        var dependency = new RunnerProviderReadinessDependency(
            Options.Create(new RunnerAvailabilityOptions
            {
                Provider = RuntimeProvider.Kubernetes
            }),
            provider,
            health);

        await dependency.CheckAsync(CancellationToken.None);

        await kubernetes.Received(1).CheckAvailabilityAsync(CancellationToken.None);
        await docker.DidNotReceive().CheckAvailabilityAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Missing_configured_provider_probe_fails_readiness()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var health = CreateHealth();
        var dependency = new RunnerProviderReadinessDependency(
            Options.Create(new RunnerAvailabilityOptions
            {
                Provider = RuntimeProvider.Libvirt
            }),
            provider,
            health);

        var action = () => dependency.CheckAsync(CancellationToken.None);

        await Assert.That(action).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Recent_provider_resource_failure_blocks_readiness_until_recovery_or_cooldown()
    {
        var docker = Substitute.For<IRuntimeProviderAvailabilityProbe>();
        docker.Provider.Returns(RuntimeProvider.Docker);
        var services = new ServiceCollection();
        services.AddKeyedSingleton(RuntimeProvider.Docker, docker);
        await using var provider = services.BuildServiceProvider();
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-19T00:00:00Z"));
        var health = CreateHealth(clock, holdSeconds: 60);
        var dependency = new RunnerProviderReadinessDependency(
            Options.Create(new RunnerAvailabilityOptions
            {
                Provider = RuntimeProvider.Docker
            }),
            provider,
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
            Options.Create(new RunnerAvailabilityOptions
            {
                ProviderFailureHoldSeconds = holdSeconds
            }),
            timeProvider ?? TimeProvider.System,
            NullLogger<RunnerProviderHealthState>.Instance);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }
}
