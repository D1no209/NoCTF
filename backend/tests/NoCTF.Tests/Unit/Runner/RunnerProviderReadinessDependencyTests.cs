using Microsoft.Extensions.DependencyInjection;
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
        var dependency = new RunnerProviderReadinessDependency(
            Options.Create(new RunnerAvailabilityOptions
            {
                Provider = RuntimeProvider.Kubernetes
            }),
            provider);

        await dependency.CheckAsync(CancellationToken.None);

        await kubernetes.Received(1).CheckAvailabilityAsync(CancellationToken.None);
        await docker.DidNotReceive().CheckAvailabilityAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Missing_configured_provider_probe_fails_readiness()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var dependency = new RunnerProviderReadinessDependency(
            Options.Create(new RunnerAvailabilityOptions
            {
                Provider = RuntimeProvider.Libvirt
            }),
            provider);

        var action = () => dependency.CheckAsync(CancellationToken.None);

        await Assert.That(action).Throws<InvalidOperationException>();
    }
}
