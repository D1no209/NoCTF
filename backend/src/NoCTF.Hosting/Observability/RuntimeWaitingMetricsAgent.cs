using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Observability;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Hosting.Observability;

internal sealed class RuntimeWaitingMetricsAgent(
    IDbContextFactory<NoCtfDbContext> contexts,
    TimeProvider clock,
    ILogger<RuntimeWaitingMetricsAgent> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), clock);
        do
        {
            try
            {
                await using var db = await contexts.CreateDbContextAsync(stoppingToken);
                var queued = db.RuntimeInstances.AsNoTracking()
                    .Where(item => item.State == RuntimeState.Queued);
                var count = await queued.CountAsync(stoppingToken);
                var oldest = count == 0
                    ? null
                    : await queued.MinAsync(item => (DateTimeOffset?)item.CreatedAt,
                        stoppingToken);
                var age = oldest is null
                    ? 0
                    : Math.Max(0, (long)(clock.GetUtcNow() - oldest.Value).TotalSeconds);
                NoCtfTelemetry.SetRuntimeWaitingSnapshot(count, age);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not refresh Runtime waiting metrics.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
