using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Infrastructure.BackgroundWork;

public sealed class CompetitionLifecycleHostedService(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    IConfiguration configuration,
    ILogger<CompetitionLifecycleHostedService> logger) : BackgroundService
{
    private readonly TimeSpan interval = TimeSpan.FromSeconds(
        Math.Max(1, configuration.GetValue("CompetitionLifecycle:IntervalSeconds", 5)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, lifetime.ApplicationStopping);
        var ct = linked.Token;
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var lifecycle = scope.ServiceProvider.GetRequiredService<AdvanceCompetitionLifecycle>();
                var transitions = await lifecycle.ExecuteAsync(DateTimeOffset.UtcNow, ct);
                var scheduler = scope.ServiceProvider.GetRequiredService<IBackgroundWorkScheduler>();
                var cache = scope.ServiceProvider.GetRequiredService<ILeaderboardCache>();
                foreach (var transition in transitions)
                {
                    await cache.InvalidateAsync(transition.CompetitionId, ct);
                    await scheduler.EnqueueLeaderboardRefreshAsync(transition.CompetitionId, ct);
                    logger.LogInformation("Competition {CompetitionId} transitioned from {From} to {To}",
                        transition.CompetitionId, transition.From, transition.To);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Competition lifecycle sweep failed");
            }
        }
    }
}
