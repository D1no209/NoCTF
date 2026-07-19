using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Infrastructure.BackgroundWork;

public sealed class LeaderboardRefreshHostedService(ChannelBackgroundWorkScheduler scheduler, IServiceScopeFactory scopeFactory, ILogger<LeaderboardRefreshHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in scheduler.ProjectionReader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ILeaderboardCache>().RefreshAsync(item.CompetitionId, stoppingToken);
            }
            catch (Exception exception) { logger.LogError(exception, "Leaderboard refresh failed for {CompetitionId}", item.CompetitionId); }
            finally { scheduler.CompleteRefresh(item.CompetitionId); }
        }
    }
}
