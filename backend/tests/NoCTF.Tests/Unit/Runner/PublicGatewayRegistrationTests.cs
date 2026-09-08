using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Runtime.PublicAccess;
using NoCTF.Domain.Platform;
using NoCTF.Runner.Composition;
using NoCTF.Runtime.Docker.PublicAccess;
using NoCTF.Hosting.Health;
using NoCTF.Runner.PublicAccess;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NoCTF.Tests.Unit.Runner;

public sealed class PublicGatewayRegistrationTests
{
    [Test]
    [Arguments("PublicGateway:ConnectorId", "")]
    [Arguments("PublicGateway:RunnerId", "other-runner")]
    public async Task Unconfigured_or_non_designated_runner_has_no_gateway_readiness(string key, string value)
    {
        var config = Configuration(); config[key] = value;
        var services = new ServiceCollection(); services.AddLogging(); services.AddNoCtfRunner(config);
        await Assert.That(services.Any(item => item.ImplementationType == typeof(PublicGatewayReadinessDependency))).IsFalse();
    }

    [Test]
    public async Task Configured_but_missing_agent_is_a_critical_readiness_failure()
    {
        var config = Configuration(); config["PublicGateway:Transport"] = "invalid";
        var services = new ServiceCollection(); services.AddLogging(); services.AddNoCtfRunner(config);
        await Assert.That(services.Count(item => item.ImplementationType == typeof(PublicGatewayReadinessDependency))).IsEqualTo(1);
        using var provider = services.BuildServiceProvider();
        var dependency = new PublicGatewayReadinessDependency(provider);
        await Assert.That(dependency.FailureIsCritical).IsTrue();
        var health = await new RoleReadinessHealthCheck([dependency]).CheckHealthAsync(new HealthCheckContext());
        await Assert.That(health.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(health.Description).Contains("public-gateway");
        await Assert.That(health.Exception).IsNull();
    }

    [Test]
    public async Task Shared_SSH_is_opt_in_and_is_rejected_on_non_Linux_runners()
    {
        var config = Configuration();
        var services = new ServiceCollection(); services.AddLogging();
        services.AddNoCtfRunner(config);
        var capability = (PublicGatewayCapability)services.Last(item => item.ServiceType == typeof(PublicGatewayCapability)).ImplementationInstance!;
        await Assert.That(capability.Transport).IsEqualTo(PublicGatewayTransportKind.SharedSsh);
        await Assert.That(capability.NamespaceIsolationAvailable).IsEqualTo(OperatingSystem.IsLinux());
        await Assert.That(services.Any(item => item.ServiceType == typeof(IPublicGatewayTransport) && item.ImplementationType == typeof(DockerSharedSshGateway)))
            .IsEqualTo(OperatingSystem.IsLinux());
        await Assert.That(capability.ReservedPorts).Contains(60999).And.Contains(60998).And.Contains(36632);
        await Assert.That(services.Count(item => item.ImplementationType == typeof(PublicGatewayReadinessDependency))).IsEqualTo(1);
        if (OperatingSystem.IsLinux())
        {
            services.AddSingleton(TimeProvider.System);
            using var provider = services.BuildServiceProvider(validateScopes: true);
            await Assert.That(provider.GetRequiredService<IPublicGatewayTransport>()).IsTypeOf<DockerSharedSshGateway>();
        }
    }

    [Test]
    [Arguments("PublicGateway:Transport", "arbitrary")]
    [Arguments("PublicGateway:LastPort", "60999")]
    [Arguments("PublicGateway:FirstPort", "1024")]
    [Arguments("PublicGateway:ClientImage", "floating:latest")]
    [Arguments("PublicGateway:LocalStateDirectory", "/state/../etc")]
    public async Task Unsafe_deployment_configuration_never_registers_a_transport(string key, string value)
    {
        var config = Configuration(); config[key] = value;
        var services = new ServiceCollection(); services.AddLogging(); services.AddNoCtfRunner(config);
        var capability = (PublicGatewayCapability)services.Last(item => item.ServiceType == typeof(PublicGatewayCapability)).ImplementationInstance!;
        await Assert.That(capability.NamespaceIsolationAvailable).IsFalse();
        await Assert.That(services.Any(item => item.ServiceType == typeof(IPublicGatewayTransport))).IsFalse();
    }

    private static IConfigurationRoot Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["RunnerScoring:CallbackBaseUrl"] = "https://api.internal",
        ["RunnerScoring:SigningKey"] = new string('x', 32),
        ["ConnectionStrings:Redis"] = "localhost:6379",
        ["Runner:Provider"] = "Docker", ["Runner:Id"] = "runner-1", ["Runner:Pool"] = "default",
        ["PublicGateway:ConnectorId"] = "test-connector", ["PublicGateway:RunnerId"] = "runner-1",
        ["PublicGateway:Transport"] = "SharedSsh", ["PublicGateway:NamespaceIsolationAvailable"] = "true",
        ["PublicGateway:FirstPort"] = "40000", ["PublicGateway:LastPort"] = "40255", ["PublicGateway:MaximumPorts"] = "8",
        ["PublicGateway:ApprovedOrigins:0"] = "https://gateway.example.test",
        ["PublicGateway:ClientImage"] = "example.test/client@sha256:" + new string('a', 64),
        ["PublicGateway:RelayImage"] = "example.test/relay@sha256:" + new string('b', 64),
        ["PublicGateway:ServerHost"] = "gateway.example.test", ["PublicGateway:ServerPort"] = "60999",
        ["PublicGateway:PrivateKeyFile"] = "/keys/key", ["PublicGateway:KnownHostsFile"] = "/keys/known_hosts",
        ["PublicGateway:LocalStateDirectory"] = "/state", ["PublicGateway:HostStateDirectory"] = "/opt/noctf/state"
    }).Build();
}
