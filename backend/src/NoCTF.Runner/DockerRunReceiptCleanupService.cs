using NoCTF.Container.Docker;

namespace NoCTF.Runner;

public sealed class DockerRunReceiptCleanupService(
    DockerProvider provider,
    ILogger<DockerRunReceiptCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CleanupAsync(stoppingToken);
        using var timer = new PeriodicTimer(CleanupInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await CleanupAsync(stoppingToken);
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await provider.CleanupExpiredRunReceiptsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Docker runtime receipt and orphan cleanup failed.");
        }
    }
}
