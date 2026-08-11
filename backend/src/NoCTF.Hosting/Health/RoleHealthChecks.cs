using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using StackExchange.Redis;
using Wolverine.Configuration;
using Wolverine.Runtime;
using Wolverine.Transports;

namespace NoCTF.Hosting.Health;

public interface IReadinessDependency
{
    string Name { get; }
    bool FailureIsCritical { get; }
    Task CheckAsync(CancellationToken cancellationToken);
}

public sealed class RoleReadinessHealthCheck(
    IEnumerable<IReadinessDependency> dependencies) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(
            dependencies.Select(dependency => CheckAsync(dependency, cancellationToken)));
        var critical = results
            .Where(result => !result.Succeeded && result.FailureIsCritical)
            .Select(result => result.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (critical.Length > 0)
            return HealthCheckResult.Unhealthy(
                $"Required dependencies unavailable: {string.Join(", ", critical)}.");

        var degraded = results
            .Where(result => !result.Succeeded)
            .Select(result => result.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return degraded.Length > 0
            ? HealthCheckResult.Degraded(
                $"Optional dependencies unavailable: {string.Join(", ", degraded)}.")
            : HealthCheckResult.Healthy();
    }

    private static async Task<DependencyResult> CheckAsync(
        IReadinessDependency dependency,
        CancellationToken cancellationToken)
    {
        try
        {
            await dependency.CheckAsync(cancellationToken);
            return new(dependency.Name, dependency.FailureIsCritical, true);
        }
        catch
        {
            return new(dependency.Name, dependency.FailureIsCritical, false);
        }
    }

    private sealed record DependencyResult(
        string Name,
        bool FailureIsCritical,
        bool Succeeded);
}

public sealed class PostgreSqlReadinessDependency(string connectionString)
    : IReadinessDependency
{
    public string Name => "postgresql";
    public bool FailureIsCritical => true;

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        _ = await command.ExecuteScalarAsync(cancellationToken);
    }
}

public sealed class WolverineReadinessDependency(IWolverineRuntime runtime)
    : IReadinessDependency
{
    public string Name => "wolverine";
    public bool FailureIsCritical => true;

    public Task CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        runtime.AssertHasStarted();
        var unhealthy = runtime.Endpoints.CollectEndpointHealth()
            .Any(EndpointIsUnavailable);
        return unhealthy
            ? Task.FromException(new InvalidOperationException(
                "One or more Wolverine endpoints are unavailable."))
            : Task.CompletedTask;
    }

    private static bool EndpointIsUnavailable(EndpointHealthSnapshot endpoint) =>
        endpoint.SenderLatched
        || endpoint.ConnectionState is TransportConnectionState.Disconnected
            or TransportConnectionState.Reconnecting
        || (endpoint.Direction == EndpointDirection.Listening
            && endpoint.ReceiveLoopStatus is ReceiveLoopStatus.NotStarted
                or ReceiveLoopStatus.Stopped
                or ReceiveLoopStatus.Faulted);
}

public sealed class RedisReadinessDependency(
    IConnectionMultiplexer redis,
    bool failureIsCritical) : IReadinessDependency
{
    public string Name => "redis";
    public bool FailureIsCritical { get; } = failureIsCritical;

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (!redis.IsConnected)
            throw new InvalidOperationException("Redis is disconnected.");
        _ = await redis.GetDatabase().PingAsync().WaitAsync(cancellationToken);
    }
}

public static class RoleHealthCheckRegistration
{
    private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(2);

    public static IServiceCollection AddNoCtfRoleHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration,
        HostRoles roles,
        bool development = false)
    {
        if (!development)
        {
            var postgres = configuration.GetConnectionString("PostgreSql")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:PostgreSql is required for readiness checks.");
            services.AddSingleton<IReadinessDependency>(
                new PostgreSqlReadinessDependency(postgres));
            services.AddSingleton<IReadinessDependency, WolverineReadinessDependency>();
            if (roles.Has(HostRole.Api)
                || roles.Has(HostRole.Worker)
                || roles.Has(HostRole.Runner))
            {
                services.AddSingleton<IReadinessDependency>(provider =>
                    new RedisReadinessDependency(
                        provider.GetRequiredService<IConnectionMultiplexer>(),
                        failureIsCritical: roles.Has(HostRole.Worker)
                            || roles.Has(HostRole.Runner)));
            }
        }

        services.AddHealthChecks().AddCheck<RoleReadinessHealthCheck>(
            "role-readiness",
            tags: ["ready"],
            timeout: ReadinessTimeout);
        return services;
    }

    public static IEndpointRouteBuilder MapNoCtfHealthChecks(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready")
        });
        return endpoints;
    }
}
