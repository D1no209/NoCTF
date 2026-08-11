using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NoCTF.Hosting;
using NoCTF.Hosting.Health;
using NSubstitute;
using StackExchange.Redis;
using Wolverine.Runtime;

namespace NoCTF.Tests.Unit.Hosting;

public sealed class RoleReadinessHealthCheckTests
{
    [Test]
    public async Task Healthy_when_every_dependency_is_available()
    {
        var healthCheck = new RoleReadinessHealthCheck(
        [
            new StubDependency("postgresql", failureIsCritical: true),
            new StubDependency("redis", failureIsCritical: false)
        ]);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
    }

    [Test]
    public async Task Required_dependency_failure_is_unhealthy_without_exception_details()
    {
        var healthCheck = new RoleReadinessHealthCheck(
        [
            new StubDependency(
                "postgresql",
                failureIsCritical: true,
                new InvalidOperationException("Password=must-not-leak"))
        ]);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Description).Contains("postgresql");
        await Assert.That(result.Description).DoesNotContain("must-not-leak");
        await Assert.That(result.Exception).IsNull();
    }

    [Test]
    public async Task Optional_dependency_failure_is_degraded()
    {
        var healthCheck = new RoleReadinessHealthCheck(
        [
            new StubDependency(
                "redis",
                failureIsCritical: false,
                new InvalidOperationException("unavailable"))
        ]);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Degraded);
        await Assert.That(result.Description).Contains("redis");
    }

    [Test]
    public async Task Role_registration_distinguishes_optional_api_and_required_runner_redis()
    {
        var apiDependencies = ResolveDependencies(HostRoles.Only(HostRole.Api));
        var runnerDependencies = ResolveDependencies(HostRoles.Only(HostRole.Runner));
        var workerDependencies = ResolveDependencies(HostRoles.Only(HostRole.Worker));

        await Assert.That(apiDependencies.OfType<RedisReadinessDependency>().Single()
                .FailureIsCritical)
            .IsFalse();
        await Assert.That(runnerDependencies.OfType<RedisReadinessDependency>().Single()
                .FailureIsCritical)
            .IsTrue();
        await Assert.That(workerDependencies.OfType<RedisReadinessDependency>())
            .IsEmpty();
        await Assert.That(workerDependencies.OfType<PostgreSqlReadinessDependency>())
            .HasSingleItem();
        await Assert.That(workerDependencies.OfType<WolverineReadinessDependency>())
            .HasSingleItem();
    }

    [Test]
    public async Task Readiness_check_has_a_bounded_two_second_timeout()
    {
        var services = new ServiceCollection();
        services.AddNoCtfRoleHealthChecks(
            new ConfigurationBuilder().Build(),
            HostRoles.Only(HostRole.Api),
            development: true);
        await using var provider = services.BuildServiceProvider();

        var registration = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single(item => item.Name == "role-readiness");

        await Assert.That(registration.Timeout).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(registration.Tags).Contains("ready");
    }

    private static IReadOnlyList<IReadinessDependency> ResolveDependencies(HostRoles roles)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IWolverineRuntime>());
        services.AddSingleton(Substitute.For<IConnectionMultiplexer>());
        services.AddNoCtfRoleHealthChecks(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSql"] = "Host=localhost;Database=noctf"
            }).Build(),
            roles);
        using var provider = services.BuildServiceProvider();
        return provider.GetServices<IReadinessDependency>().ToArray();
    }

    private sealed class StubDependency(
        string name,
        bool failureIsCritical,
        Exception? failure = null) : IReadinessDependency
    {
        public string Name { get; } = name;
        public bool FailureIsCritical { get; } = failureIsCritical;

        public Task CheckAsync(CancellationToken cancellationToken) =>
            failure is null ? Task.CompletedTask : Task.FromException(failure);
    }
}
