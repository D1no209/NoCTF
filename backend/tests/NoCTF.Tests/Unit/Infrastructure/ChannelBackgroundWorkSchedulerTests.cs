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
}
