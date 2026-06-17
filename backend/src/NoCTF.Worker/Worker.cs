using System.Text.Json;
using NoCTF.Application.BackgroundTasks;
using NoCTF.PluginBase;

namespace NoCTF.Worker;

public class Worker(IServiceScopeFactory scopeFactory, ILogger<Worker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("NoCTF worker started.");
        await RecoverExpiredTasksAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var queue = scope.ServiceProvider.GetRequiredService<IBackgroundTaskQueue>();
                var task = await queue.TryAcquireNextAsync(LockDuration, stoppingToken);

                if (task is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                try
                {
                    await ExecuteTaskAsync(scope.ServiceProvider, task.Type, task.PayloadJson, stoppingToken);
                    await queue.MarkSucceededAsync(task.Id, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Background task {TaskId} of type {TaskType} failed.", task.Id, task.Type);
                    await queue.MarkFailedAsync(task.Id, ex, CancellationToken.None);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker loop failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task RecoverExpiredTasksAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IBackgroundTaskQueue>();
        var recovered = await queue.RecoverExpiredRunningTasksAsync(LockDuration, stoppingToken);
        if (recovered > 0)
            logger.LogWarning("Recovered {Count} expired running background tasks.", recovered);
    }

    private static async Task ExecuteTaskAsync(
        IServiceProvider services,
        string type,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        switch (type)
        {
            case BackgroundTaskTypes.AwdpPatchValidation:
            {
                var payload = JsonSerializer.Deserialize<AwdpPatchValidationPayload>(payloadJson, JsonOptions)
                    ?? throw new InvalidOperationException("Invalid AWDP patch validation payload.");
                var patchService = services.GetRequiredService<IAwdpPatchService>();
                await patchService.ValidatePatchAsync(payload.SubmissionId, cancellationToken);
                break;
            }
            default:
                throw new NotSupportedException($"Unknown background task type '{type}'.");
        }
    }
}
