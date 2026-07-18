using NoCTF.Application.Competitions.Lifecycle;

namespace NoCTF.Worker;

public sealed class LifecycleWorker(IServiceScopeFactory scopeFactory, ILogger<LifecycleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AdvanceCompetitionLifecycle>()
                    .ExecuteAsync(DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Competition lifecycle tick failed.");
            }
        }
    }
}
