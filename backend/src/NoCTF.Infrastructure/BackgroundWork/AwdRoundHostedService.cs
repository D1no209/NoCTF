using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.SystemProducers;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.BackgroundWork;

public sealed class AwdRoundHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AwdRoundHostedService> logger) : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("SystemProducers:AwdSweepSeconds", 5)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<ProvisionModeRuntimes>()
                    .ExecuteAsync(GameMode.Awd, stoppingToken);
                var flags = await scope.ServiceProvider.GetRequiredService<RotateAwdFlags>()
                    .ExecuteAsync(DateTimeOffset.UtcNow, stoppingToken);
                var checks = await scope.ServiceProvider.GetRequiredService<ProduceAwdChecks>()
                    .ExecuteAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (result.FailedCount > 0)
                    logger.LogWarning("AWD runtime sweep failed for {FailedCount} operations", result.FailedCount);
                if (flags.FailedCount > 0)
                    logger.LogWarning("AWD flag rotation deferred {FailedTargetCount} targets", flags.FailedCount);
                if (checks.FailedCount > 0)
                    logger.LogWarning("AWD checker dispatch deferred {FailedTargetCount} targets", checks.FailedCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "AWD runtime sweep failed"); }
        }
    }
}
