using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.SystemProducers;

namespace NoCTF.Infrastructure.BackgroundWork;

public sealed class KohPollingHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<KohPollingHostedService> logger) : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1,
        configuration.GetValue("SystemProducers:KohSweepSeconds", 1)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<ProduceKohObservations>()
                    .ExecuteAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (result.CreatedCount > 0)
                    logger.LogInformation("KoH polling recorded {CreatedEventCount} facts for {TargetCount} targets",
                        result.CreatedCount, result.TargetCount);
                if (result.FailedCount > 0)
                    logger.LogWarning("KoH polling deferred {FailedTargetCount} invalid targets until the next sweep",
                        result.FailedCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "KoH polling sweep failed");
            }
        }
    }
}
