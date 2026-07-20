using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NoCTF.Infrastructure.BackgroundWork;

/// <summary>Stops external admission, then drains Channel stages in dependency order.</summary>
public sealed class BackgroundWorkShutdownCoordinator : IHostedService, IDisposable
{
    private readonly ChannelBackgroundWorkScheduler scheduler;
    private readonly ILogger<BackgroundWorkShutdownCoordinator> logger;
    private readonly TimeSpan drainTimeout;
    private readonly IDisposable stoppingRegistration;
    private readonly Dictionary<BackgroundWorkDrainStage, TaskCompletionSource<Task>> stageRegistrations = new()
    {
        [BackgroundWorkDrainStage.Maintenance] = NewRegistration(),
        [BackgroundWorkDrainStage.Processing] = NewRegistration(),
        [BackgroundWorkDrainStage.Projection] = NewRegistration()
    };
    private int stopping;

    public BackgroundWorkShutdownCoordinator(
        ChannelBackgroundWorkScheduler scheduler,
        BackgroundQueueOptions options,
        IHostApplicationLifetime lifetime,
        ILogger<BackgroundWorkShutdownCoordinator> logger)
    {
        this.scheduler = scheduler;
        this.logger = logger;
        drainTimeout = TimeSpan.FromSeconds(Math.Clamp(options.ShutdownDrainSeconds, 1, 300));
        stoppingRegistration = lifetime.ApplicationStopping.Register(BeginShutdown);
    }

    public CancellationToken DrainToken => scheduler.DrainToken;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        BeginShutdown();
        scheduler.BeginDrain(drainTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, DrainToken);
        try
        {
            await scheduler.WaitForAdmissionsToDrainAsync(linked.Token);

            scheduler.CompleteMaintenanceWriter();
            await WaitForStageAsync(BackgroundWorkDrainStage.Maintenance, linked.Token);

            scheduler.CompleteProcessingWriters();
            await WaitForStageAsync(BackgroundWorkDrainStage.Processing, linked.Token);

            scheduler.CompleteProjectionWriter();
            await WaitForStageAsync(BackgroundWorkDrainStage.Projection, linked.Token);
            logger.LogInformation("Background work drain completed");
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            scheduler.CancelDrain();
            logger.LogWarning("Background work drain exceeded its bounded shutdown window");
        }
        finally
        {
            scheduler.CompleteAllWriters();
        }
    }

    public void BeginShutdown()
    {
        if (Interlocked.Exchange(ref stopping, 1) == 1) return;

        scheduler.BeginShutdown();
        logger.LogInformation("Submission admission stopped; background consumers have {DrainTimeoutSeconds}s to drain", drainTimeout.TotalSeconds);
    }

    public void RegisterStage(BackgroundWorkDrainStage stage, Task completion) =>
        stageRegistrations[stage].TrySetResult(completion);

    private async Task WaitForStageAsync(BackgroundWorkDrainStage stage, CancellationToken cancellationToken)
    {
        var completion = await stageRegistrations[stage].Task.WaitAsync(cancellationToken);
        await completion.WaitAsync(cancellationToken);
    }

    private static TaskCompletionSource<Task> NewRegistration() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Dispose()
    {
        stoppingRegistration.Dispose();
    }
}

public enum BackgroundWorkDrainStage
{
    Maintenance,
    Processing,
    Projection
}
