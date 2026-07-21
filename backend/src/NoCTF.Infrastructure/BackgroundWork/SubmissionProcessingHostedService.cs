using NoCTF.Application.Submissions.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NoCTF.Infrastructure.BackgroundWork;

/// <summary>Consumes in-process submission work with bounded concurrency and transient retries.</summary>
public sealed class SubmissionProcessingHostedService(
    ChannelBackgroundWorkScheduler scheduler,
    BackgroundWorkShutdownCoordinator shutdown,
    IServiceScopeFactory scopeFactory,
    BackgroundQueueOptions options,
    ILogger<SubmissionProcessingHostedService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var completion = Task.WhenAll(
            Enumerable.Range(0, options.ProcessingConcurrency).Select(_ => ConsumeAsync(shutdown.DrainToken)));
        shutdown.RegisterStage(BackgroundWorkDrainStage.Processing, completion);
        return completion;
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        await foreach (var item in scheduler.ProcessingReader.ReadAllAsync(ct))
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    if (item is ProcessSubmissionWorkItem submission)
                    {
                        var processor = scope.ServiceProvider.GetRequiredService<ISubmissionProcessor>();
                        await processor.ProcessAsync(submission.SubmissionId, ct);
                    }
                    else if (item is ProcessSystemEventWorkItem systemEvent)
                    {
                        var processor = scope.ServiceProvider.GetRequiredService<ISystemScoringEventProcessor>();
                        await processor.ProcessAsync(systemEvent.ScoringEventId, ct);
                    }
                    break;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (attempt < 3 && !ct.IsCancellationRequested)
                {
                    logger.LogWarning(exception, "Processing work {WorkItemType} {WorkItemId} failed on attempt {Attempt}",
                        item.GetType().Name, GetId(item), attempt + 1);
                    await Task.Delay(TimeSpan.FromSeconds(attempt switch { 0 => 1, 1 => 5, _ => 15 }), ct);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Processing work {WorkItemType} {WorkItemId} exhausted retries",
                        item.GetType().Name, GetId(item));
                }
            }
        }
    }

    private static Guid GetId(ProcessingWorkItem item) => item switch
    {
        ProcessSubmissionWorkItem submission => submission.SubmissionId,
        ProcessSystemEventWorkItem systemEvent => systemEvent.ScoringEventId,
        _ => Guid.Empty
    };
}
