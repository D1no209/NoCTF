using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Infrastructure.BackgroundWork;

public sealed class LeaderboardRefreshHostedService(
    ChannelBackgroundWorkScheduler scheduler,
    BackgroundWorkShutdownCoordinator shutdown,
    IServiceScopeFactory scopeFactory,
    BackgroundQueueOptions options,
    ILogger<LeaderboardRefreshHostedService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var completion = Task.WhenAll(
            Enumerable.Range(0, options.ProjectionConcurrency).Select(_ => ConsumeAsync(shutdown.DrainToken)));
        shutdown.RegisterStage(BackgroundWorkDrainStage.Projection, completion);
        return completion;
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        await foreach (var item in scheduler.ProjectionReader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ILeaderboardCache>().RefreshAsync(item.CompetitionId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Leaderboard refresh failed for {CompetitionId}", item.CompetitionId); }
            finally { scheduler.CompleteRefresh(item.CompetitionId); }
        }
    }
}
