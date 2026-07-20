using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Runtime;

namespace NoCTF.Infrastructure.BackgroundWork;

public sealed class OrphanRuntimeCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<OrphanRuntimeCleanupHostedService> logger) : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(Math.Max(30,
        configuration.GetValue("Runtime:OrphanCleanupIntervalSeconds", 60)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<OrphanRuntimeCleaner>()
                    .ExecuteAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (result.StoppedCount > 0)
                    logger.LogInformation("Cleaned up {StoppedRuntimeCount} orphan runtime instances", result.StoppedCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Orphan runtime cleanup sweep failed");
            }
        }
    }
}
