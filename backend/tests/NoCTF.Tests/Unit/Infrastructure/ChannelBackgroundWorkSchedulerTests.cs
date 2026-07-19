using NoCTF.Infrastructure.BackgroundWork;

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
}
