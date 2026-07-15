using Docker.DotNet;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using StackExchange.Redis;

namespace NoCTF.API;

public class PostgreSqlHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(timeoutCts.Token);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync(timeoutCts.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL health check failed.", ex);
        }
    }
}

public class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            if (!redis.IsConnected)
                return HealthCheckResult.Unhealthy("Redis not connected");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
            await redis.GetDatabase().PingAsync().WaitAsync(timeoutCts.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis health check failed.", ex);
        }
    }
}

public class DockerHealthCheck(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

            var runnerBaseUrl = configuration["Runner:BaseUrl"];
            if (!string.IsNullOrWhiteSpace(runnerBaseUrl))
            {
                var httpClient = httpClientFactory.CreateClient();
                httpClient.BaseAddress = new Uri(runnerBaseUrl);
                using var response = await httpClient.GetAsync("/runner/health", timeoutCts.Token);
                return response.IsSuccessStatusCode
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy($"Runner health returned {(int)response.StatusCode}");
            }

            var dockerHost = configuration["Docker:Host"];
            using var config = dockerHost is not null
                ? new DockerClientConfiguration(new Uri(dockerHost))
                : new DockerClientConfiguration();
            using var client = config.CreateClient();
            await client.System.GetVersionAsync(timeoutCts.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Container runtime health check failed.", ex);
        }
    }
}
