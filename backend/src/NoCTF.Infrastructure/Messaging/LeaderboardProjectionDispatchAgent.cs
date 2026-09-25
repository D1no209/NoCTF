using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Messaging;
using NoCTF.Application.Observability;
using Wolverine;

namespace NoCTF.Infrastructure.Messaging;

public sealed class LeaderboardProjectionDispatchAgent(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    LeaderboardProjectionMergeQueue mergeQueue,
    ILogger<LeaderboardProjectionDispatchAgent> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromMilliseconds(50),
            timeProvider);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var now = timeProvider.GetUtcNow();
            foreach (var competitionId in mergeQueue.TakeDue(now))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await bus.PublishAsync(new ProjectLeaderboard(competitionId));
                    NoCtfTelemetry.RecordLeaderboardMergeDispatch("success");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    mergeQueue.Retry(competitionId, now.AddSeconds(1));
                    NoCtfTelemetry.RecordLeaderboardMergeDispatch("failure");
                    logger.LogError(
                        exception,
                        "Worker failed to publish merged leaderboard projection.");
                }
            }
        }
    }
}
