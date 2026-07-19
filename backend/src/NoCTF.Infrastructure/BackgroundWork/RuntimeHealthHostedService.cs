using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Runtime;

namespace NoCTF.Infrastructure.BackgroundWork;

public sealed class RuntimeHealthHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<RuntimeHealthHostedService> logger) : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(Math.Max(10,
        configuration.GetValue("Runtime:HealthIntervalSeconds", 30)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var changed = await scope.ServiceProvider.GetRequiredService<ChallengeRuntimeHealthChecker>()
                    .ExecuteAsync(stoppingToken);
                if (changed > 0)
                    logger.LogInformation("Runtime health checker updated {ChangedRuntimeCount} instances", changed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Runtime health check failed");
            }
        }
    }
}
