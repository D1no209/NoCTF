using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.SystemProducers;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.BackgroundWork;

public sealed class AwdpRoundHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AwdpRoundHostedService> logger) : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("SystemProducers:AwdpSweepSeconds", 5)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<ProvisionModeRuntimes>()
                    .ExecuteAsync(GameMode.Awdp, stoppingToken);
                if (result.FailedCount > 0)
                    logger.LogWarning("AWDP runtime sweep failed for {FailedCount} operations", result.FailedCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "AWDP runtime sweep failed"); }
        }
    }
}
