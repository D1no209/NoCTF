using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.Hosting;
using NoCTF.Hosting.Health;
using NSubstitute;
using StackExchange.Redis;
using Wolverine.Configuration;
using Wolverine.Runtime;
using Wolverine.Transports;

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
        await Assert.That(result.Data["postgresql.status"])
            .IsEqualTo(HealthStatus.Healthy.ToString());
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
        await Assert.That(result.Data["postgresql.status"])
            .IsEqualTo(HealthStatus.Unhealthy.ToString());
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
        await Assert.That(result.Data["redis.status"])
            .IsEqualTo(HealthStatus.Degraded.ToString());
    }

    [Test]
    public async Task Role_registration_distinguishes_optional_api_and_required_worker_runner_redis()
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
        await Assert.That(workerDependencies.OfType<RedisReadinessDependency>().Single()
                .FailureIsCritical)
            .IsTrue();
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
        var logging = provider.GetRequiredService<IOptions<LoggerFilterOptions>>().Value;
        await Assert.That(logging.Rules.Any(rule =>
                rule.CategoryName
                    == "Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService"
                && rule.LogLevel == LogLevel.Critical))
            .IsTrue();
    }

    [Test]
    public async Task Readiness_transition_state_suppresses_unchanged_probe_noise()
    {
        var state = new RoleReadinessLogState();

        await Assert.That(state.TryTransition(
            HealthStatus.Unhealthy,
            ["postgresql"]))
            .IsTrue();
        await Assert.That(state.TryTransition(
            HealthStatus.Unhealthy,
            ["postgresql"]))
            .IsFalse();
        await Assert.That(state.TryTransition(
            HealthStatus.Healthy,
            []))
            .IsTrue();
    }

    [Test]
    public async Task Unchanged_failure_is_logged_once_and_recovery_is_logged_once()
    {
        var state = new RoleReadinessLogState();
        var logger = new RecordingLogger();
        var unhealthy = new RoleReadinessHealthCheck(
        [
            new StubDependency(
                "postgresql",
                failureIsCritical: true,
                new InvalidOperationException("unavailable"))
        ], state, logger);

        _ = await unhealthy.CheckHealthAsync(new HealthCheckContext());
        _ = await unhealthy.CheckHealthAsync(new HealthCheckContext());
        _ = await new RoleReadinessHealthCheck(
                [new StubDependency("postgresql", failureIsCritical: true)],
                state,
                logger)
            .CheckHealthAsync(new HealthCheckContext());

        await Assert.That(logger.Levels)
            .IsEquivalentTo([LogLevel.Error, LogLevel.Information]);
    }

    [Test]
    public async Task Health_check_execution_resolves_scoped_readiness_dependencies()
    {
        var services = new ServiceCollection();
        services.AddScoped<IReadinessDependency, ScopedReadinessDependency>();
        services.AddNoCtfRoleHealthChecks(
            new ConfigurationBuilder().Build(),
            HostRoles.Only(HostRole.Worker),
            development: true);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        var report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(
                registration => registration.Name == "role-readiness",
                CancellationToken.None);

        await Assert.That(report.Status).IsEqualTo(HealthStatus.Healthy);
    }

    [Test]
    [Arguments(TransportConnectionState.Unknown, ReceiveLoopStatus.Unknown, false)]
    [Arguments(TransportConnectionState.Connected, ReceiveLoopStatus.Running, false)]
    [Arguments(TransportConnectionState.Disconnected, ReceiveLoopStatus.Running, true)]
    [Arguments(TransportConnectionState.Reconnecting, ReceiveLoopStatus.Running, true)]
    [Arguments(TransportConnectionState.Connected, ReceiveLoopStatus.NotStarted, true)]
    [Arguments(TransportConnectionState.Connected, ReceiveLoopStatus.Stopped, true)]
    [Arguments(TransportConnectionState.Connected, ReceiveLoopStatus.Faulted, true)]
    public async Task Wolverine_readiness_accepts_unreported_health_but_rejects_explicit_failures(
        TransportConnectionState connectionState,
        ReceiveLoopStatus receiveLoopStatus,
        bool expected)
    {
        var endpoint = CreateEndpointHealthSnapshot(
            EndpointDirection.Listening,
            connectionState,
            receiveLoopStatus);

        var dependency = CreateWolverineDependency(endpoint);
        Func<Task> action = () => dependency.CheckAsync(CancellationToken.None);

        if (expected)
            await Assert.That(action).Throws<InvalidOperationException>();
        else
            await action();
    }

    [Test]
    public async Task Wolverine_readiness_rejects_a_latched_sender()
    {
        var endpoint = CreateEndpointHealthSnapshot(
            EndpointDirection.Sending,
            TransportConnectionState.Unknown,
            ReceiveLoopStatus.Unknown,
            senderLatched: true);

        var dependency = CreateWolverineDependency(endpoint);
        Func<Task> action = () => dependency.CheckAsync(CancellationToken.None);

        await Assert.That(action).Throws<InvalidOperationException>();
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

    private static EndpointHealthSnapshot CreateEndpointHealthSnapshot(
        EndpointDirection direction,
        TransportConnectionState connectionState,
        ReceiveLoopStatus receiveLoopStatus,
        bool senderLatched = false) => new(
            new Uri("stub://health-test"),
            "health-test",
            direction,
            "test",
            0,
            null,
            null,
            senderLatched,
            null,
            null,
            connectionState,
            receiveLoopStatus,
            null);

    private static WolverineReadinessDependency CreateWolverineDependency(
        EndpointHealthSnapshot endpoint)
    {
        var runtime = Substitute.For<IWolverineRuntime>();
        var endpoints = Substitute.For<IEndpointCollection>();
        runtime.Endpoints.Returns(endpoints);
        endpoints.CollectEndpointHealth().Returns([endpoint]);
        return new(runtime);
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

    private sealed class RecordingLogger : ILogger<RoleReadinessHealthCheck>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Levels.Add(logLevel);
    }

    private sealed class ScopedReadinessDependency : IReadinessDependency
    {
        public string Name => "scoped";
        public bool FailureIsCritical => true;
        public Task CheckAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
