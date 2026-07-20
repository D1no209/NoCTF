using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;

namespace NoCTF.Tests.Unit.Application;

public class RecordSystemScoringEventTests
{
    [Test]
    public async Task ExecuteAsync_NewSource_QueuesSystemEventProcessing()
    {
        var competition = Guid.NewGuid();
        var store = new Store(new(Guid.NewGuid(), true));
        var scheduler = new Scheduler();
        var useCase = new RecordSystemScoringEvent(store, scheduler, scheduler);

        var result = await useCase.ExecuteAsync(Command(competition));

        await Assert.That(result.Created).IsTrue();
        await Assert.That(scheduler.SystemEvent).IsEqualTo(result.ScoringEventId);
    }

    [Test]
    public async Task ExecuteAsync_DuplicateSource_DoesNotQueueAgain()
    {
        var competition = Guid.NewGuid();
        var scheduler = new Scheduler();
        var useCase = new RecordSystemScoringEvent(new Store(new(Guid.NewGuid(), false)), scheduler, scheduler);

        var result = await useCase.ExecuteAsync(Command(competition));

        await Assert.That(result.Created).IsFalse();
        await Assert.That(scheduler.SystemEvent).IsNull();
    }

    [Test]
    public async Task ExecuteAsync_AdmissionStopped_DoesNotPersistSystemFact()
    {
        var store = new Store(new(Guid.NewGuid(), true));
        var scheduler = new Scheduler { Accepting = false };
        var useCase = new RecordSystemScoringEvent(store, scheduler, scheduler);

        var execute = async () => await useCase.ExecuteAsync(Command(Guid.NewGuid()));

        await Assert.That(execute).Throws<BackgroundWorkUnavailableException>();
        await Assert.That(store.RecordCalls).IsEqualTo(0);
    }

    private static RecordSystemScoringEventCommand Command(Guid competition) => new(
        competition, Guid.NewGuid(), Guid.NewGuid(), ScoringEventKind.AwdServiceCheck,
        ScoringResult.Correct, null, DateTimeOffset.UtcNow, "test-v1", "source-1");

    private sealed class Store(RecordSystemScoringEventResult result) : ISystemScoringEventStore
    {
        public int RecordCalls { get; private set; }
        public Task<RecordSystemScoringEventResult> RecordAsync(RecordSystemScoringEventCommand command, CancellationToken cancellationToken)
        {
            RecordCalls++;
            return Task.FromResult(result);
        }
    }

    private sealed class Scheduler : IBackgroundWorkScheduler, IBackgroundWorkAdmissionGate
    {
        public Guid? SystemEvent { get; private set; }
        public bool Accepting { get; init; } = true;
        public bool IsAccepting => Accepting;
        public IBackgroundWorkAdmissionLease? TryEnter() => Accepting ? new Lease() : null;
        public ValueTask EnqueueSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EnqueueSystemEventAsync(Guid scoringEventId, CancellationToken cancellationToken) { SystemEvent = scoringEventId; return ValueTask.CompletedTask; }
        public ValueTask EnqueueLeaderboardRefreshAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EnqueueCompetitionRebuildAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        private sealed class Lease : IBackgroundWorkAdmissionLease
        {
            public CancellationToken DrainCancellation => CancellationToken.None;
            public void Dispose() { }
        }
    }
}
