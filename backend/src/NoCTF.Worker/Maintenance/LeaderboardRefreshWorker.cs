using NoCTF.Application.Scoring.Ports;

namespace NoCTF.Worker;

public sealed class LeaderboardRefreshWorker(IServiceScopeFactory scopeFactory, ILogger<LeaderboardRefreshWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<ILeaderboardStore>();
                foreach (var competitionId in await store.GetDirtyCompetitionsAsync(stoppingToken))
                {
                    var snapshot = await store.GetAuthoritativeAsync(competitionId, stoppingToken);
                    if (snapshot is null) continue;
                    await store.PublishCacheAsync(snapshot, stoppingToken);
                    await store.MarkCleanAsync(competitionId, snapshot.ProjectionVersion, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Leaderboard refresh tick failed.");
            }
        }
    }
}
