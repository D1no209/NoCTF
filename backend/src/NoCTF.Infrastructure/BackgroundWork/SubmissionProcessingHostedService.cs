using NoCTF.Application.Submissions.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NoCTF.Infrastructure.BackgroundWork;

/// <summary>Consumes in-process submission work with bounded concurrency and transient retries.</summary>
public sealed class SubmissionProcessingHostedService(
    ChannelBackgroundWorkScheduler scheduler,
    IServiceScopeFactory scopeFactory,
    BackgroundQueueOptions options,
    ILogger<SubmissionProcessingHostedService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
        Enumerable.Range(0, options.ProcessingConcurrency).Select(_ => ConsumeAsync(stoppingToken)));

    private async Task ConsumeAsync(CancellationToken ct)
    {
        await foreach (var item in scheduler.ProcessingReader.ReadAllAsync(ct))
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<ISubmissionProcessor>();
                    await processor.ProcessAsync(item.SubmissionId, ct);
                    break;
                }
                catch (Exception exception) when (attempt < 3 && !ct.IsCancellationRequested)
                {
                    logger.LogWarning(exception, "Submission work {SubmissionId} failed on attempt {Attempt}", item.SubmissionId, attempt + 1);
                    await Task.Delay(TimeSpan.FromSeconds(attempt switch { 0 => 1, 1 => 5, _ => 15 }), ct);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Submission work {SubmissionId} exhausted retries", item.SubmissionId);
                }
            }
        }
    }
}
