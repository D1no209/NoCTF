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
                .Select(competition => competition.UpdatedAt)
                .ToArrayAsync(cancellationToken);
            var waiting = await db.RuntimeInstances
                .AsNoTracking()
                .Where(runtime => runtime.State == RuntimeState.Queued)
                .Select(runtime => runtime.CreatedAt)
                .ToArrayAsync(cancellationToken);

            NoCtfTelemetry.UpdateOperationalSnapshot(
                dirty.LongLength,
                OldestAge(now, dirty),
                waiting.LongLength,
                OldestAge(now, waiting));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Operational metric collection failed.");
        }
    }

    private static TimeSpan OldestAge(DateTimeOffset now, IReadOnlyCollection<DateTimeOffset> values)
    {
        if (values.Count == 0) return TimeSpan.Zero;
        var age = now - values.Min();
        return age > TimeSpan.Zero ? age : TimeSpan.Zero;
    }
}
