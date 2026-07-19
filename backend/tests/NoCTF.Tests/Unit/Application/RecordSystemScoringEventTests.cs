using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;

namespace NoCTF.Tests.Unit.Application;

public class RecordSystemScoringEventTests
{
    [Test]
    public async Task ExecuteAsync_NewSource_InvalidatesAndQueuesRefresh()
    {
        var competition = Guid.NewGuid();
        var store = new Store(new(Guid.NewGuid(), true));
        var cache = new Cache();
        var scheduler = new Scheduler();
        var useCase = new RecordSystemScoringEvent(store, cache, scheduler);

        var result = await useCase.ExecuteAsync(Command(competition));

        await Assert.That(result.Created).IsTrue();
        await Assert.That(cache.Invalidated).IsEqualTo(competition);
        await Assert.That(scheduler.Refresh).IsEqualTo(competition);
    }

    [Test]
    public async Task ExecuteAsync_DuplicateSource_DoesNotQueueAgain()
    {
        var competition = Guid.NewGuid();
        var cache = new Cache();
        var scheduler = new Scheduler();
        var useCase = new RecordSystemScoringEvent(new Store(new(Guid.NewGuid(), false)), cache, scheduler);

        var result = await useCase.ExecuteAsync(Command(competition));

        await Assert.That(result.Created).IsFalse();
        await Assert.That(cache.Invalidated).IsNull();
        await Assert.That(scheduler.Refresh).IsNull();
    }

    private static RecordSystemScoringEventCommand Command(Guid competition) => new(
        competition, Guid.NewGuid(), Guid.NewGuid(), ScoringEventKind.AwdServiceCheck,
        ScoringResult.Correct, null, DateTimeOffset.UtcNow, "test-v1", "source-1");

    private sealed class Store(RecordSystemScoringEventResult result) : ISystemScoringEventStore
    {
        public Task<RecordSystemScoringEventResult> RecordAsync(RecordSystemScoringEventCommand command, CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class Cache : ILeaderboardCache
    {
        public Guid? Invalidated { get; private set; }
        public Task<LeaderboardResponse?> GetAsync(Guid competitionId, CancellationToken cancellationToken) => Task.FromResult<LeaderboardResponse?>(null);
        public Task RefreshAsync(Guid competitionId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task InvalidateAsync(Guid competitionId, CancellationToken cancellationToken) { Invalidated = competitionId; return Task.CompletedTask; }
    }

    private sealed class Scheduler : IBackgroundWorkScheduler
    {
        public Guid? Refresh { get; private set; }
        public ValueTask EnqueueSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EnqueueSystemEventAsync(Guid scoringEventId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EnqueueLeaderboardRefreshAsync(Guid competitionId, CancellationToken cancellationToken) { Refresh = competitionId; return ValueTask.CompletedTask; }
        public ValueTask EnqueueCompetitionRebuildAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
