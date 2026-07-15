using NoCTF.Application.Events;

namespace NoCTF.Tests;

public class LeaderboardRefreshCoordinatorTests
{
    [Fact]
    public async Task BurstForOneCompetition_CoalescesIntoOneRefresh()
    {
        var coordinator = new LeaderboardRefreshCoordinator(debounce: TimeSpan.FromMilliseconds(30));
        var competitionId = Guid.NewGuid();
        var refreshes = 0;

        var requests = Enumerable.Range(0, 20)
            .Select(_ => coordinator.EnqueueAsync(competitionId, () =>
            {
                Interlocked.Increment(ref refreshes);
                return Task.CompletedTask;
            }))
            .ToArray();

        await Task.WhenAll(requests);

        Assert.Equal(1, refreshes);
        Assert.Equal(0, coordinator.TrackedStateCount);
    }

    [Fact]
    public async Task RequestDuringRefresh_RunsLatestWorkBeforeRetiring()
    {
        var coordinator = new LeaderboardRefreshCoordinator(debounce: TimeSpan.Zero);
        var competitionId = Guid.NewGuid();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCalls = 0;
        var secondCalls = 0;

        var first = coordinator.EnqueueAsync(competitionId, async () =>
        {
            Interlocked.Increment(ref firstCalls);
            firstStarted.TrySetResult();
            await releaseFirst.Task;
        });
        await firstStarted.Task;
        var second = coordinator.EnqueueAsync(competitionId, () =>
        {
            Interlocked.Increment(ref secondCalls);
            return Task.CompletedTask;
        });
        releaseFirst.TrySetResult();

        await Task.WhenAll(first, second);

        Assert.Equal(1, firstCalls);
        Assert.Equal(1, secondCalls);
        Assert.Equal(0, coordinator.TrackedStateCount);
    }

    [Fact]
    public async Task FailedRefresh_RemovesStateAndAllowsRetry()
    {
        var coordinator = new LeaderboardRefreshCoordinator(debounce: TimeSpan.Zero);
        var competitionId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.EnqueueAsync(
            competitionId,
            () => Task.FromException(new InvalidOperationException("refresh failed"))));
        Assert.Equal(0, coordinator.TrackedStateCount);

        var retryCalls = 0;
        await coordinator.EnqueueAsync(competitionId, () =>
        {
            retryCalls++;
            return Task.CompletedTask;
        });

        Assert.Equal(1, retryCalls);
        Assert.Equal(0, coordinator.TrackedStateCount);
    }
}
