using NoCTF.Application.Messaging;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public class CompetitionLifecycleUseCaseTests
{
    [Test]
    public async Task Resume_QueuesRuntimeProvision()
    {
        var competitionId = Guid.NewGuid();
        var scheduler = new Scheduler();
        var useCase = new TransitionCompetitionLifecycle(
            new Store(CompetitionStatus.Paused), new Cache(), scheduler);

        var result = await useCase.ExecuteAsync(
            competitionId, CompetitionStatus.Running, Guid.NewGuid(), "resume");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(scheduler.Provisioned).IsEqualTo(competitionId);
        await Assert.That(scheduler.Cleaned).IsNull();
    }

    [Test]
    public async Task Finish_QueuesRuntimeCleanup()
    {
        var competitionId = Guid.NewGuid();
        var scheduler = new Scheduler();
        var useCase = new TransitionCompetitionLifecycle(
            new Store(CompetitionStatus.Running), new Cache(), scheduler);

        var result = await useCase.ExecuteAsync(
            competitionId, CompetitionStatus.Finished, Guid.NewGuid(), "finish");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(scheduler.Cleaned).IsEqualTo(competitionId);
        await Assert.That(scheduler.Provisioned).IsNull();
    }

    private sealed class Store(CompetitionStatus status) : ICompetitionLifecycleStore
    {
        public Task<CompetitionStatus?> GetStatusAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionStatus?>(status);

        public Task<IReadOnlyList<CompetitionLifecycleSnapshot>> GetDueAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CompetitionLifecycleSnapshot>>([]);

        public Task<bool> TryTransitionAsync(
            Guid competitionId,
            CompetitionStatus from,
            CompetitionStatus to,
            CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class Cache : ILeaderboardCache
    {
        public Task<LeaderboardResponse?> GetAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult<LeaderboardResponse?>(null);
        public Task RefreshAsync(Guid competitionId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task InvalidateAsync(Guid competitionId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Scheduler : IBackendMessagePublisher
    {
        public Guid? Provisioned { get; private set; }
        public Guid? Cleaned { get; private set; }
        public ValueTask ProjectLeaderboardAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RebuildCompetitionAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask CleanupCompetitionRuntimesAsync(Guid competitionId, CancellationToken cancellationToken)
        {
            Cleaned = competitionId;
            return ValueTask.CompletedTask;
        }
        public ValueTask ProvisionCompetitionRuntimesAsync(Guid competitionId, CancellationToken cancellationToken)
        {
            Provisioned = competitionId;
            return ValueTask.CompletedTask;
        }
    }
}
