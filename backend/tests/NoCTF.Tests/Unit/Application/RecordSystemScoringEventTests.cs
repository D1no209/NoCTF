using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awdp.Scoring;

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

    [Test]
    public async Task ExecuteAsync_FinishedCompetition_DoesNotPersistOrEnterAdmission()
    {
        var store = new Store(new(Guid.NewGuid(), true)) { Status = CompetitionStatus.Finished };
        var scheduler = new Scheduler();

        var result = await new RecordSystemScoringEvent(store, scheduler, scheduler)
            .ExecuteAsync(Command(Guid.NewGuid()));

        await Assert.That(result.Failure).IsEqualTo(SystemScoringEventRecordFailure.CompetitionFinished);
        await Assert.That(store.RecordCalls).IsEqualTo(0);
        await Assert.That(scheduler.AdmissionCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_RejectsAwdpFailureCodeOnAnotherEventKind()
    {
        var store = new Store(new(Guid.NewGuid(), true));
        var scheduler = new Scheduler();
        var useCase = new RecordSystemScoringEvent(store, scheduler, scheduler);
        var command = Command(Guid.NewGuid()) with
        {
            Kind = ScoringEventKind.KohObservation,
            Result = ScoringResult.Rejected,
            FailureCode = ScoringFailureCode.AwdpViolation
        };

        var execute = async () => await useCase.ExecuteAsync(command);

        await Assert.That(execute).Throws<ArgumentException>();
        await Assert.That(store.RecordCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ExecuteAsync_AllowsAwdServiceCheckForAwdMode()
    {
        var store = new Store(new(Guid.NewGuid(), true)) { Mode = GameMode.Awd };
        var scheduler = new Scheduler();
        var useCase = new RecordSystemScoringEvent(store, scheduler, scheduler);
        var command = Command(Guid.NewGuid()) with
        {
            Kind = ScoringEventKind.AwdServiceCheck,
            Result = ScoringResult.Wrong
        };

        var result = await useCase.ExecuteAsync(command);

        await Assert.That(result.Created).IsTrue();
        await Assert.That(store.LastCommand!.Kind).IsEqualTo(ScoringEventKind.AwdServiceCheck);
    }

    [Test]
    public async Task ExecuteAsync_RejectsAwdpFixCheckOutsideDedicatedUseCase()
    {
        var store = new Store(new(Guid.NewGuid(), true));
        var scheduler = new Scheduler();
        var useCase = new RecordSystemScoringEvent(store, scheduler, scheduler);
        var command = Command(Guid.NewGuid()) with { Kind = ScoringEventKind.AwdpFixCheck };

        var execute = async () => await useCase.ExecuteAsync(command);

        await Assert.That(execute).Throws<ArgumentException>();
        await Assert.That(store.RecordCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ExecutePenetrationStageAsync_RequiresStageAndInstanceDimensions()
    {
        var store = new Store(new(Guid.NewGuid(), true));
        var scheduler = new Scheduler();
        var useCase = new RecordSystemScoringEvent(store, scheduler, scheduler);
        var command = Command(Guid.NewGuid()) with
        {
            Kind = ScoringEventKind.PenetrationStage,
            Result = ScoringResult.PlatformFailed,
            FailureCode = ScoringFailureCode.ProducerUnavailable
        };

        var execute = async () => await useCase.ExecutePenetrationStageAsync(command);

        await Assert.That(execute).Throws<ArgumentException>();
        await Assert.That(store.RecordCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ExecutePenetrationStageAsync_AcceptsFullyScopedPlatformFailure()
    {
        var store = new Store(new(Guid.NewGuid(), true));
        var scheduler = new Scheduler();
        var useCase = new RecordSystemScoringEvent(store, scheduler, scheduler);
        var command = Command(Guid.NewGuid()) with
        {
            Kind = ScoringEventKind.PenetrationStage,
            Result = ScoringResult.PlatformFailed,
            FailureCode = ScoringFailureCode.ProducerUnavailable,
            StageId = Guid.NewGuid(),
            ChallengeInstanceId = Guid.NewGuid()
        };

        var result = await useCase.ExecutePenetrationStageAsync(command);

        await Assert.That(result.Created).IsTrue();
        await Assert.That(store.LastCommand!.StageId).IsEqualTo(command.StageId);
        await Assert.That(store.LastCommand.ChallengeInstanceId).IsEqualTo(command.ChallengeInstanceId);
    }

    [Test]
    public async Task RecordAwdpCheckResult_MapsExitCodeBeforeRecordingFact()
    {
        var store = new Store(new(Guid.NewGuid(), true));
        var scheduler = new Scheduler();
        var record = new RecordSystemScoringEvent(store, scheduler, scheduler);
        var useCase = new RecordAwdpCheckResult(new AwdpCheckExitCodeMapper(), store, record);

        var result = await useCase.ExecuteAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), AwdpVerificationPhase.Checker, 2, false,
            DateTimeOffset.UtcNow, "awdp-check:1"));

        await Assert.That(result.Created).IsTrue();
        await Assert.That(store.LastCommand!.Kind).IsEqualTo(ScoringEventKind.AwdpFixCheck);
        await Assert.That(store.LastCommand.Result).IsEqualTo(ScoringResult.Rejected);
        await Assert.That(store.LastCommand.FailureCode).IsEqualTo(ScoringFailureCode.AwdpViolation);
    }

    [Test]
    public async Task RecordAwdpCheckResult_RejectsNonAwdpCompetition()
    {
        var store = new Store(new(Guid.NewGuid(), true)) { Mode = GameMode.Ctf };
        var scheduler = new Scheduler();
        var record = new RecordSystemScoringEvent(store, scheduler, scheduler);
        var useCase = new RecordAwdpCheckResult(new AwdpCheckExitCodeMapper(), store, record);

        var result = await useCase.ExecuteAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), AwdpVerificationPhase.Checker, 0, false,
            DateTimeOffset.UtcNow, "awdp-check:wrong-mode"));

        await Assert.That(result.Failure).IsEqualTo(SystemScoringEventRecordFailure.CompetitionModeMismatch);
        await Assert.That(store.RecordCalls).IsEqualTo(0);
    }

    [Test]
    public async Task RecordAwdpCheckResult_ReturnsNotFoundForMissingCompetition()
    {
        var store = new Store(new(Guid.NewGuid(), true)) { Mode = null };
        var scheduler = new Scheduler();
        var record = new RecordSystemScoringEvent(store, scheduler, scheduler);
        var useCase = new RecordAwdpCheckResult(new AwdpCheckExitCodeMapper(), store, record);

        var result = await useCase.ExecuteAsync(new(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), AwdpVerificationPhase.Checker, 0, false,
            DateTimeOffset.UtcNow, "awdp-check:missing"));

        await Assert.That(result.Failure).IsEqualTo(SystemScoringEventRecordFailure.CompetitionNotFound);
        await Assert.That(store.RecordCalls).IsEqualTo(0);
    }

    [Test]
    public async Task RecordAwdpCheckResult_MapsPatchTimeoutToTypedViolationFact()
    {
        var store = new Store(new(Guid.NewGuid(), true));
        var scheduler = new Scheduler();
        var useCase = new RecordAwdpCheckResult(new AwdpCheckExitCodeMapper(), store,
            new RecordSystemScoringEvent(store, scheduler, scheduler));

        await useCase.ExecuteAsync(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            AwdpVerificationPhase.Patch, -1, true, DateTimeOffset.UtcNow, "awdp-patch:timeout"));

        await Assert.That(store.LastCommand!.Result).IsEqualTo(ScoringResult.Rejected);
        await Assert.That(store.LastCommand.FailureCode).IsEqualTo(ScoringFailureCode.AwdpPatchTimeout);
    }

    private static RecordSystemScoringEventCommand Command(Guid competition) => new(
        competition, Guid.NewGuid(), Guid.NewGuid(), ScoringEventKind.SystemInput,
        ScoringResult.Correct, null, DateTimeOffset.UtcNow, "test-v1", "source-1");

    private sealed class Store(RecordSystemScoringEventResult result) : ISystemScoringEventStore
    {
        public GameMode? Mode { get; init; } = GameMode.Awdp;
        public RecordSystemScoringEventCommand? LastCommand { get; private set; }
        public Task<GameMode?> GetCompetitionModeAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult(Mode);
        public CompetitionStatus? Status { get; init; } = CompetitionStatus.Running;
        public int RecordCalls { get; private set; }
        public Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken cancellationToken) => Task.FromResult(Status);
        public Task<RecordSystemScoringEventResult> RecordAsync(RecordSystemScoringEventCommand command, CancellationToken cancellationToken)
        {
            RecordCalls++;
            LastCommand = command;
            return Task.FromResult(result);
        }
    }

    private sealed class Scheduler : IBackgroundWorkScheduler, IBackgroundWorkAdmissionGate
    {
        public Guid? SystemEvent { get; private set; }
        public int AdmissionCalls { get; private set; }
        public bool Accepting { get; init; } = true;
        public bool IsAccepting => Accepting;
        public IBackgroundWorkAdmissionLease? TryEnter()
        {
            AdmissionCalls++;
            return Accepting ? new Lease() : null;
        }
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
