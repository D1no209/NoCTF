using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.SystemProducers;

namespace NoCTF.Infrastructure.BackgroundWork;

public sealed class PenetrationStageMonitorHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<PenetrationStageMonitorHostedService> logger) : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1,
        configuration.GetValue("SystemProducers:PenetrationStageSweepSeconds", 5)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<MonitorPenetrationStages>()
                    .ExecuteAsync(stoppingToken);
                if (result.CreatedCount > 0)
                    logger.LogInformation(
                        "Penetration stage monitor recorded {CreatedEventCount} facts for {TargetCount} failed instances",
                        result.CreatedCount,
                        result.TargetCount);
                if (result.FailedCount > 0)
                    logger.LogWarning(
                        "Penetration stage monitor deferred {FailedTargetCount} invalid targets",
                        result.FailedCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Penetration stage monitor sweep failed");
            }
        }
    }
}
