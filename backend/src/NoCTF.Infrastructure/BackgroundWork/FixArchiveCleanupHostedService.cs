using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Submissions.Processing;

namespace NoCTF.Infrastructure.BackgroundWork;

public sealed class FixArchiveCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<FixArchiveCleanupHostedService> logger) : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(Math.Max(5,
        configuration.GetValue("FixVerification:CleanupIntervalSeconds", 30)));
    private readonly int batchSize = Math.Clamp(configuration.GetValue("FixVerification:CleanupBatchSize", 32), 1, 256);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<CleanupFixArchives>()
                    .ExecuteAsync(batchSize, DateTimeOffset.UtcNow, stoppingToken);
                if (result.DeletedCount > 0 || result.FailedCount > 0)
                    logger.LogInformation("Fix archive cleanup completed with {DeletedCount} deleted and {FailedCount} failed",
                        result.DeletedCount, result.FailedCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Fix archive cleanup sweep failed");
            }
        }
    }
}
