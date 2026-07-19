using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Maintenance;
using NoCTF.Application.BackgroundWork;

namespace NoCTF.Infrastructure.BackgroundWork;

/// <summary>Consumes deduplicated competition rebuild work from the maintenance Channel.</summary>
public sealed class CompetitionRebuildHostedService(
    ChannelBackgroundWorkScheduler scheduler,
    IServiceScopeFactory scopeFactory,
    ILogger<CompetitionRebuildHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in scheduler.MaintenanceReader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ExecuteWithRetryAsync(item, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            finally
            {
                scheduler.CompleteRebuild(item.CompetitionId);
            }
        }
    }

    private async Task ExecuteWithRetryAsync(
        RebuildCompetitionWorkItem item,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider
                    .GetRequiredService<ICompetitionRebuildProcessor>()
                    .RebuildAsync(item.CompetitionId, ct);
                await scope.ServiceProvider
                    .GetRequiredService<IBackgroundWorkScheduler>()
                    .EnqueueLeaderboardRefreshAsync(item.CompetitionId, ct);
                logger.LogInformation("Competition rebuild completed for {CompetitionId}", item.CompetitionId);
                return;
            }
            catch (Exception exception) when (attempt < 3 && !ct.IsCancellationRequested)
            {
                logger.LogWarning(exception,
                    "Competition rebuild {CompetitionId} failed on attempt {Attempt}",
                    item.CompetitionId,
                    attempt + 1);
                await Task.Delay(TimeSpan.FromSeconds(attempt switch { 0 => 1, 1 => 5, _ => 15 }), ct);
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Competition rebuild {CompetitionId} exhausted retries",
                    item.CompetitionId);
                return;
            }
        }
    }
}
