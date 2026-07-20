using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Infrastructure.BackgroundWork;
using NoCTF.Application.BackgroundWork;

namespace NoCTF.Tests.Unit.Infrastructure;

public class BackgroundWorkShutdownCoordinatorTests
{
    [Test]
    public async Task ApplicationStopping_StopsNewSubmissionAdmissionImmediately()
    {
        using var scheduler = new ChannelBackgroundWorkScheduler(new BackgroundQueueOptions());
        using var lifetime = new TestHostApplicationLifetime();
        using var coordinator = new BackgroundWorkShutdownCoordinator(
            scheduler,
            new BackgroundQueueOptions(),
            lifetime,
            NullLogger<BackgroundWorkShutdownCoordinator>.Instance);

        lifetime.StopApplication();

        await Assert.That(scheduler.IsAccepting).IsFalse();
        await Assert.That(scheduler.TryEnter()).IsNull();
    }

    [Test]
    public async Task StopAsync_WaitsForAdmissionAndDrainStagesInDependencyOrder()
    {
        using var scheduler = new ChannelBackgroundWorkScheduler(new BackgroundQueueOptions());
        using var lifetime = new TestHostApplicationLifetime();
        using var coordinator = new BackgroundWorkShutdownCoordinator(
            scheduler,
            new BackgroundQueueOptions { ShutdownDrainSeconds = 10 },
            lifetime,
            NullLogger<BackgroundWorkShutdownCoordinator>.Instance);
        var maintenance = NewStage();
        var processing = NewStage();
        var projection = NewStage();
        coordinator.RegisterStage(BackgroundWorkDrainStage.Maintenance, maintenance.Task);
        coordinator.RegisterStage(BackgroundWorkDrainStage.Processing, processing.Task);
        coordinator.RegisterStage(BackgroundWorkDrainStage.Projection, projection.Task);
        var admission = scheduler.TryEnter();
        await Assert.That(admission).IsNotNull();

        var stop = coordinator.StopAsync(CancellationToken.None);
        await Task.Yield();
        await Assert.That(stop.IsCompleted).IsFalse();
        await Assert.That(scheduler.TryEnter()).IsNull();

        admission!.Dispose();
        await Task.Yield();
        await Assert.That(stop.IsCompleted).IsFalse();

        maintenance.SetResult();
        await Task.Yield();
        await Assert.That(stop.IsCompleted).IsFalse();

        processing.SetResult();
        await Task.Yield();
        await Assert.That(stop.IsCompleted).IsFalse();

        projection.SetResult();
        await stop;
        await Assert.That(stop.IsCompletedSuccessfully).IsTrue();

        var enqueue = async () => await scheduler.EnqueueLeaderboardRefreshAsync(Guid.NewGuid(), CancellationToken.None);
        await Assert.That(enqueue).Throws<BackgroundWorkUnavailableException>();
    }

    [Test]
    public async Task StopAsync_CancelsDrainAfterConfiguredTimeout()
    {
        using var scheduler = new ChannelBackgroundWorkScheduler(new BackgroundQueueOptions());
        using var lifetime = new TestHostApplicationLifetime();
        using var coordinator = new BackgroundWorkShutdownCoordinator(
            scheduler,
            new BackgroundQueueOptions { ShutdownDrainSeconds = 1 },
            lifetime,
            NullLogger<BackgroundWorkShutdownCoordinator>.Instance);
        coordinator.RegisterStage(BackgroundWorkDrainStage.Maintenance, NewStage().Task);
        coordinator.RegisterStage(BackgroundWorkDrainStage.Processing, NewStage().Task);
        coordinator.RegisterStage(BackgroundWorkDrainStage.Projection, NewStage().Task);
        var stopwatch = Stopwatch.StartNew();

        await coordinator.StopAsync(CancellationToken.None);

        await Assert.That(coordinator.DrainToken.IsCancellationRequested).IsTrue();
        await Assert.That(stopwatch.Elapsed).IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900));
        await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task StopAsync_HostTimeoutCancelsConsumersImmediately()
    {
        using var scheduler = new ChannelBackgroundWorkScheduler(new BackgroundQueueOptions());
        using var lifetime = new TestHostApplicationLifetime();
        using var coordinator = new BackgroundWorkShutdownCoordinator(
            scheduler,
            new BackgroundQueueOptions { ShutdownDrainSeconds = 30 },
            lifetime,
            NullLogger<BackgroundWorkShutdownCoordinator>.Instance);
        coordinator.RegisterStage(BackgroundWorkDrainStage.Maintenance, NewStage().Task);
        coordinator.RegisterStage(BackgroundWorkDrainStage.Processing, NewStage().Task);
        coordinator.RegisterStage(BackgroundWorkDrainStage.Projection, NewStage().Task);
        using var hostTimeout = new CancellationTokenSource();

        var stop = coordinator.StopAsync(hostTimeout.Token);
        hostTimeout.Cancel();
        await stop;

        await Assert.That(coordinator.DrainToken.IsCancellationRequested).IsTrue();
    }

    private static TaskCompletionSource NewStage() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource started = new();
        private readonly CancellationTokenSource stopping = new();
        private readonly CancellationTokenSource stopped = new();

        public CancellationToken ApplicationStarted => started.Token;
        public CancellationToken ApplicationStopping => stopping.Token;
        public CancellationToken ApplicationStopped => stopped.Token;
        public void StopApplication() => stopping.Cancel();

        public void Dispose()
        {
            started.Dispose();
            stopping.Dispose();
            stopped.Dispose();
        }
    }
}
