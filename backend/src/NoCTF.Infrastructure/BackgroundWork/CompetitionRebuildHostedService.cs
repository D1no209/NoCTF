using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Maintenance;
using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Runtime;

namespace NoCTF.Infrastructure.BackgroundWork;

/// <summary>Consumes deduplicated competition rebuild work from the maintenance Channel.</summary>
public sealed class CompetitionRebuildHostedService(
    ChannelBackgroundWorkScheduler scheduler,
    BackgroundWorkShutdownCoordinator shutdown,
    IServiceScopeFactory scopeFactory,
    BackgroundQueueOptions options,
    ILogger<CompetitionRebuildHostedService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var completion = Task.WhenAll(
            Enumerable.Range(0, options.MaintenanceConcurrency).Select(_ => ConsumeAsync(shutdown.DrainToken)));
        shutdown.RegisterStage(BackgroundWorkDrainStage.Maintenance, completion);
        return completion;
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        await foreach (var item in scheduler.MaintenanceReader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await ExecuteWithRetryAsync(item, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            finally
            {
                if (item is RebuildCompetitionWorkItem) scheduler.CompleteRebuild(item.CompetitionId);
                else if (item is CleanupCompetitionRuntimeWorkItem) scheduler.CompleteRuntimeCleanup(item.CompetitionId);
                else scheduler.CompleteRuntimeProvision(item.CompetitionId);
            }
        }
    }

    private async Task ExecuteWithRetryAsync(
        MaintenanceWorkItem item,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                if (item is RebuildCompetitionWorkItem)
                {
                    await scope.ServiceProvider
                        .GetRequiredService<ICompetitionRebuildProcessor>()
                        .RebuildAsync(item.CompetitionId, ct);
                    await scope.ServiceProvider
                        .GetRequiredService<IBackgroundWorkScheduler>()
                        .EnqueueLeaderboardRefreshAsync(item.CompetitionId, ct);
                }
                else if (item is CleanupCompetitionRuntimeWorkItem)
                {
                    await scope.ServiceProvider
                        .GetRequiredService<CompetitionRuntimeCleaner>()
                        .ExecuteAsync(item.CompetitionId, ct);
                }
                else
                {
                    var result = await scope.ServiceProvider
                        .GetRequiredService<CompetitionRuntimeProvisioner>()
                        .ExecuteAsync(item.CompetitionId, ct);
                    if (result.FailedCount > 0)
                        throw new InvalidOperationException($"{result.FailedCount} competition runtimes failed to provision.");
                }
                logger.LogInformation("Maintenance item {WorkItemType} completed for {CompetitionId}", item.GetType().Name, item.CompetitionId);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (attempt < 3 && !ct.IsCancellationRequested)
            {
                logger.LogWarning(exception,
                    "Maintenance item {WorkItemType} for {CompetitionId} failed on attempt {Attempt}",
                    item.GetType().Name,
                    item.CompetitionId,
                    attempt + 1);
                await Task.Delay(TimeSpan.FromSeconds(attempt switch { 0 => 1, 1 => 5, _ => 15 }), ct);
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Maintenance item {WorkItemType} for {CompetitionId} exhausted retries",
                    item.GetType().Name,
                    item.CompetitionId);
                return;
            }
        }
    }
}
