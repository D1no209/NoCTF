using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Observability;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Observability;

public sealed class OperationalMetricsCollector(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<OperationalMetricsCollector> logger) : BackgroundService
{
    private static readonly TimeSpan CollectionInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CollectionInterval, timeProvider);
        do
        {
            await CollectAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CollectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
            var now = timeProvider.GetUtcNow();

            var dirty = await db.Competitions
                .AsNoTracking()
                .Where(competition => competition.LeaderboardDirty)
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Count = group.LongCount(),
                    OldestAt = group.Min(competition => competition.UpdatedAt)
                })
                .SingleOrDefaultAsync(cancellationToken);
            var waiting = await db.RuntimeInstances
                .AsNoTracking()
                .Where(runtime => runtime.State == RuntimeState.Queued)
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Count = group.LongCount(),
                    OldestAt = group.Min(runtime => runtime.CreatedAt)
                })
                .SingleOrDefaultAsync(cancellationToken);

            NoCtfTelemetry.UpdateOperationalSnapshot(
                dirty?.Count ?? 0,
                OldestAge(now, dirty?.OldestAt),
                waiting?.Count ?? 0,
                OldestAge(now, waiting?.OldestAt));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Operational metric collection failed.");
        }
    }

    private static TimeSpan OldestAge(DateTimeOffset now, DateTimeOffset? oldestAt)
    {
        if (oldestAt is null) return TimeSpan.Zero;
        var age = now - oldestAt.Value;
        return age > TimeSpan.Zero ? age : TimeSpan.Zero;
    }
}
