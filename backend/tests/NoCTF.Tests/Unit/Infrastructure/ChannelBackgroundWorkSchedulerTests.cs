using NoCTF.Infrastructure.BackgroundWork;
using NoCTF.Application.BackgroundWork;

namespace NoCTF.Tests.Unit.Infrastructure;

public class ChannelBackgroundWorkSchedulerTests
{
    [Test]
    public async Task EnqueueLeaderboardRefreshAsync_CoalescesPendingCompetitionWork()
    {
        var scheduler = new ChannelBackgroundWorkScheduler(new BackgroundQueueOptions
        {
            ProcessingCapacity = 1,
            ProjectionCapacity = 2,
            MaintenanceCapacity = 1
        });
        var competitionId = Guid.NewGuid();

        await scheduler.EnqueueLeaderboardRefreshAsync(competitionId, CancellationToken.None);
        await scheduler.EnqueueLeaderboardRefreshAsync(competitionId, CancellationToken.None);

        var item = await scheduler.ProjectionReader.ReadAsync();

        await Assert.That(item.CompetitionId).IsEqualTo(competitionId);
        await Assert.That(scheduler.ProjectionReader.TryRead(out _)).IsFalse();
    }

    [Test]
    public async Task CompleteLeaderboardRefresh_AllowsLaterRefreshForSameCompetition()
    {
        var scheduler = new ChannelBackgroundWorkScheduler(new BackgroundQueueOptions
        {
            ProcessingCapacity = 1,
            ProjectionCapacity = 2,
            MaintenanceCapacity = 1
        });
        var competitionId = Guid.NewGuid();
        await scheduler.EnqueueLeaderboardRefreshAsync(competitionId, CancellationToken.None);
        _ = await scheduler.ProjectionReader.ReadAsync();
        scheduler.CompleteRefresh(competitionId);

        await scheduler.EnqueueLeaderboardRefreshAsync(competitionId, CancellationToken.None);

        var item = await scheduler.ProjectionReader.ReadAsync();
        await Assert.That(item.CompetitionId).IsEqualTo(competitionId);
    }

    [Test]
    public async Task EnqueueRuntimeCleanupAsync_CoalescesPendingCompetitionWork()
    {
        var scheduler = new ChannelBackgroundWorkScheduler(new BackgroundQueueOptions { MaintenanceCapacity = 2 });
        var competitionId = Guid.NewGuid();

        await scheduler.EnqueueRuntimeCleanupAsync(competitionId, CancellationToken.None);
        await scheduler.EnqueueRuntimeCleanupAsync(competitionId, CancellationToken.None);

        var item = await scheduler.MaintenanceReader.ReadAsync();
        await Assert.That(item).IsTypeOf<CleanupCompetitionRuntimeWorkItem>();
        await Assert.That(item.CompetitionId).IsEqualTo(competitionId);
        await Assert.That(scheduler.MaintenanceReader.TryRead(out _)).IsFalse();
    }

    [Test]
    public async Task RuntimeCleanupAndRebuild_AreIndependentMaintenanceItems()
    {
        var scheduler = new ChannelBackgroundWorkScheduler(new BackgroundQueueOptions { MaintenanceCapacity = 2 });
        var competitionId = Guid.NewGuid();

        await scheduler.EnqueueRuntimeCleanupAsync(competitionId, CancellationToken.None);
        await scheduler.EnqueueCompetitionRebuildAsync(competitionId, CancellationToken.None);

        var first = await scheduler.MaintenanceReader.ReadAsync();
        var second = await scheduler.MaintenanceReader.ReadAsync();
        await Assert.That(new[] { first.GetType(), second.GetType() })
            .IsEquivalentTo(new[] { typeof(CleanupCompetitionRuntimeWorkItem), typeof(RebuildCompetitionWorkItem) });
    }

    [Test]
    public async Task CompleteAllWriters_DrainsQueuedItemsBeforeReaderCompletes()
    {
        var scheduler = new ChannelBackgroundWorkScheduler(new BackgroundQueueOptions { ProcessingCapacity = 2 });
        var submissionId = Guid.NewGuid();
        await scheduler.EnqueueSubmissionAsync(submissionId, CancellationToken.None);

        scheduler.BeginShutdown();
        scheduler.CompleteAllWriters();

        var item = await scheduler.ProcessingReader.ReadAsync();
        await scheduler.ProcessingReader.Completion;
        await Assert.That(item).IsTypeOf<ProcessSubmissionWorkItem>();
        await Assert.That(((ProcessSubmissionWorkItem)item).SubmissionId).IsEqualTo(submissionId);
        await Assert.That(scheduler.IsAccepting).IsFalse();
    }

    [Test]
    public async Task CompleteAllWriters_RejectsNewWork()
    {
        var scheduler = new ChannelBackgroundWorkScheduler(new BackgroundQueueOptions());
        scheduler.BeginShutdown();
        scheduler.CompleteAllWriters();

        var enqueue = async () => await scheduler.EnqueueSubmissionAsync(Guid.NewGuid(), CancellationToken.None);

        await Assert.That(enqueue).Throws<BackgroundWorkUnavailableException>();
    }

    [Test]
    public async Task EnqueueSubmissionAsync_FullQueue_HonorsCancellation()
    {
        var scheduler = new ChannelBackgroundWorkScheduler(new BackgroundQueueOptions { ProcessingCapacity = 1 });
        await scheduler.EnqueueSubmissionAsync(Guid.NewGuid(), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var enqueue = async () => await scheduler.EnqueueSubmissionAsync(Guid.NewGuid(), cancellation.Token);

        await Assert.That(enqueue).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task BeginShutdown_WaitsForActiveAdmissionLease()
    {
        var scheduler = new ChannelBackgroundWorkScheduler(new BackgroundQueueOptions());
        var lease = scheduler.TryEnter();
        await Assert.That(lease).IsNotNull();

        scheduler.BeginShutdown();
        var drained = scheduler.WaitForAdmissionsToDrainAsync(CancellationToken.None);

        await Assert.That(scheduler.IsAccepting).IsFalse();
        await Assert.That(drained.IsCompleted).IsFalse();
        lease!.Dispose();
        await drained;
        await Assert.That(drained.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task SubmissionAndSystemEvent_ShareBoundedProcessingChannel()
    {
        var scheduler = new ChannelBackgroundWorkScheduler(new BackgroundQueueOptions { ProcessingCapacity = 2 });
        var submissionId = Guid.NewGuid();
        var scoringEventId = Guid.NewGuid();

        await scheduler.EnqueueSubmissionAsync(submissionId, CancellationToken.None);
        await scheduler.EnqueueSystemEventAsync(scoringEventId, CancellationToken.None);

        var first = await scheduler.ProcessingReader.ReadAsync();
        var second = await scheduler.ProcessingReader.ReadAsync();
        await Assert.That(new[] { first.GetType(), second.GetType() })
            .IsEquivalentTo(new[] { typeof(ProcessSubmissionWorkItem), typeof(ProcessSystemEventWorkItem) });
    }
}
