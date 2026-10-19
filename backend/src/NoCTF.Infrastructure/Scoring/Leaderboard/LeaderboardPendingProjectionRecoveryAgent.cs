using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Messaging;
using NoCTF.Application.Observability;
using Wolverine;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed class LeaderboardPendingProjectionRecoveryAgent(IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime, ILogger<LeaderboardPendingProjectionRecoveryAgent> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(ct);
        await using var scope = scopes.CreateAsyncScope();
        var pending = await scope.ServiceProvider.GetRequiredService<ILeaderboardPublicationFence>().GetPendingCompetitionsAsync(ct);
        if (pending.Count == 0) return;
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        foreach (var competitionId in pending)
        {
            await bus.PublishAsync(new ProjectLeaderboard(competitionId, LeaderboardRefreshMode.IfMissing));
            NoCtfTelemetry.RecordLeaderboardMergeDispatch("recovery");
        }
        logger.LogInformation("Queued {Count} pending leaderboard projections after Worker startup.", pending.Count);
    }
}
