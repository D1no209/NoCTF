using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
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
    IReadOnlyDictionary<string, object> Describe() =>
        new Dictionary<string, object>();
}

public sealed class RoleReadinessLogState
{
    private readonly object gate = new();
    private string? fingerprint;

    public bool TryTransition(HealthStatus status, IReadOnlyList<string> failures)
    {
        var next = $"{status}:{string.Join(',', failures)}";
        lock (gate)
        {
            if (string.Equals(fingerprint, next, StringComparison.Ordinal))
                return false;
            fingerprint = next;
            return true;
        }
    }
}

public sealed partial class RoleReadinessHealthCheck(
    IEnumerable<IReadinessDependency> dependencies,
    RoleReadinessLogState? logState = null,
    ILogger<RoleReadinessHealthCheck>? logger = null) : IHealthCheck
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
        var data = results
            .Select(result => new KeyValuePair<string, object>(
                $"{result.Name}.status",
                result.Succeeded
                    ? HealthStatus.Healthy.ToString()
                    : result.FailureIsCritical
                        ? HealthStatus.Unhealthy.ToString()
                        : HealthStatus.Degraded.ToString()))
            .Concat(results
            .SelectMany(result => result.Data.Select(item =>
                new KeyValuePair<string, object>($"{result.Name}.{item.Key}", item.Value))))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        if (critical.Length > 0)
        {
            ReportTransition(HealthStatus.Unhealthy, critical);
            return HealthCheckResult.Unhealthy(
                $"Required dependencies unavailable: {string.Join(", ", critical)}.",
                data: data);
        }

        var degraded = results
            .Where(result => !result.Succeeded)
            .Select(result => result.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (degraded.Length > 0)
        {
            ReportTransition(HealthStatus.Degraded, degraded);
            return HealthCheckResult.Degraded(
                $"Optional dependencies unavailable: {string.Join(", ", degraded)}.",
                data: data);
        }

        ReportTransition(HealthStatus.Healthy, []);
        return HealthCheckResult.Healthy(data: data);
    }

    private void ReportTransition(HealthStatus status, IReadOnlyList<string> failures)
    {
        if (logger is null || logState?.TryTransition(status, failures) != true)
            return;
        var names = string.Join(", ", failures);
        switch (status)
        {
            case HealthStatus.Unhealthy:
                LogUnhealthy(logger, names);
                break;
            case HealthStatus.Degraded:
                LogDegraded(logger, names);
                break;
            default:
                LogHealthy(logger);
                break;
        }
    }

    [LoggerMessage(
        EventId = 4100,
        Level = LogLevel.Error,
        Message = "Role readiness changed to Unhealthy. Failed dependencies: {Dependencies}.")]
    private static partial void LogUnhealthy(ILogger logger, string dependencies);

    [LoggerMessage(
        EventId = 4101,
        Level = LogLevel.Warning,
        Message = "Role readiness changed to Degraded. Failed optional dependencies: {Dependencies}.")]
    private static partial void LogDegraded(ILogger logger, string dependencies);

    [LoggerMessage(
        EventId = 4102,
        Level = LogLevel.Information,
        Message = "Role readiness recovered; all dependencies are available.")]
    private static partial void LogHealthy(ILogger logger);

    private static async Task<DependencyResult> CheckAsync(
        IReadinessDependency dependency,
        CancellationToken cancellationToken)
    {
        try
        {
            await dependency.CheckAsync(cancellationToken);
            return new(
                dependency.Name,
                dependency.FailureIsCritical,
                true,
                dependency.Describe());
        }
        catch
        {
            return new(
                dependency.Name,
                dependency.FailureIsCritical,
                false,
                dependency.Describe());
        }
    }

    private sealed record DependencyResult(
        string Name,
        bool FailureIsCritical,
        bool Succeeded,
        IReadOnlyDictionary<string, object> Data);
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
        services.AddLogging(logging => logging.AddFilter(
            "Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService",
            LogLevel.Critical));
        services.AddSingleton<RoleReadinessLogState>();
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
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = WriteReadinessAsync
        });
        return endpoints;
    }

    private static Task WriteReadinessAsync(
        Microsoft.AspNetCore.Http.HttpContext context,
        HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var data = report.Entries
            .SelectMany(entry => entry.Value.Data.Select(item =>
                new KeyValuePair<string, object?>(item.Key, item.Value)))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            data
        }));
    }
}
