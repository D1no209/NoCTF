using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed class LeaderboardPayloadCleanupAgent(ILeaderboardPublicationFence publications,
    ILogger<LeaderboardPayloadCleanupAgent> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await publications.CleanupAsync(stoppingToken); }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            { logger.LogError(exception, "Scoreboard payload cleanup failed."); }
        }
    }
}
