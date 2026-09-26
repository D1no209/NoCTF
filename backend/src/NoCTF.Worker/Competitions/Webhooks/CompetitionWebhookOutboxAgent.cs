using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Observability;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Infrastructure.Persistence;
using Wolverine;

namespace NoCTF.Worker.Competitions.Webhooks;

/// <summary>Replays transactionally persisted webhook events when the post-commit wakeup is lost.</summary>
public sealed class CompetitionWebhookOutboxAgent(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<CompetitionWebhookOutboxAgent> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, clock);
        var ticks = 0;
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var deliveries = scope.ServiceProvider
                    .GetRequiredService<ICompetitionWebhookDeliveryStore>();
                var pending = await deliveries.ClaimPendingOutboxAsync(
                    clock.GetUtcNow(), 64, stoppingToken);
                await Parallel.ForEachAsync(
                    pending.GroupBy(item => item.CompetitionId),
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 4,
                        CancellationToken = stoppingToken
                    },
                    async (group, token) =>
                    {
                        foreach (var item in group)
                            await DispatchAsync(item, token);
                    });
                if (++ticks % 20 == 0)
                {
                    var snapshot = await deliveries.ReadPendingSnapshotAsync(
                        clock.GetUtcNow(), stoppingToken);
                    NoCtfTelemetry.SetWebhookPendingSnapshot(
                        snapshot.PendingDeliveries, snapshot.OldestPendingAgeSeconds,
                        snapshot.UndispatchedEvents);
                }
                if (ticks % 4 == 0)
                    await CaptureDueFrozenSnapshotsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Webhook outbox wakeup failed; pending events will be retried.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DispatchAsync(
        CompetitionWebhookOutboxWakeup item, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var deliveries = scope.ServiceProvider
            .GetRequiredService<ICompetitionWebhookDeliveryStore>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        Guid? cursor = null;
        do
        {
            var batch = await deliveries.PrepareBatchAsync(
                new DispatchCompetitionWebhooks(item.CompetitionId, item.EventId, cursor),
                100, cancellationToken);
            foreach (var delivery in batch.Deliveries)
                await bus.PublishAsync(delivery);
            cursor = batch.NextAfterTargetId;
        }
        while (cursor is not null);
        await deliveries.MarkDispatchCompletedAsync(
            item.EventId, clock.GetUtcNow(), cancellationToken);
    }

    private async Task CaptureDueFrozenSnapshotsAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
        var due = await db.Competitions.AsNoTracking()
            .Where(item => item.DeletedAt == null
                && item.FrozenStartAt != null
                && item.FrozenStartAt <= now
                && item.FrozenStartAt >= now.AddSeconds(-5)
                && !db.CompetitionWebhookFrozenProjections.Any(snapshot =>
                    snapshot.CompetitionId == item.Id
                    && snapshot.FrozenAt == item.FrozenStartAt))
            .OrderBy(item => item.FrozenStartAt)
            .Take(8)
            .Select(item => new { item.Id, FrozenAt = item.FrozenStartAt!.Value })
            .ToArrayAsync(ct);
        foreach (var item in due)
        {
            try
            {
                await using var captureScope = scopes.CreateAsyncScope();
                var leaderboard = captureScope.ServiceProvider
                    .GetRequiredService<ILeaderboardCache>();
                if (await leaderboard.GetFrozenWebhookScoreboardAsync(
                        item.Id, item.FrozenAt, ct) is null)
                    logger.LogWarning(
                        "Frozen public projection was not captured for competition {CompetitionId}.",
                        item.Id);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Frozen public projection capture failed for competition {CompetitionId}.",
                    item.Id);
            }
        }
    }
}
