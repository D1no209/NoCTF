namespace NoCTF.Runner;

public sealed class KubernetesRunReceiptCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<KubernetesRunReceiptCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var manager = scope.ServiceProvider
                        .GetRequiredService<NoCTF.Container.K8s.KubernetesManager>();
                    var removed = await manager.CleanupExpiredRunReceiptsAsync(stoppingToken);
                    if (removed > 0)
                    {
                        logger.LogInformation(
                            "Removed {Count} expired Kubernetes one-shot receipts.",
                            removed);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Kubernetes one-shot receipt cleanup failed.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
