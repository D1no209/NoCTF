using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Application.SystemProducers;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;

namespace NoCTF.Tests.Unit.Application;

public sealed class PenetrationStageMonitorTests
{
    private static readonly DateTimeOffset FailedAt = new(2026, 7, 21, 1, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task FailedInstance_RecordsPlatformFailureOnlyForIncompleteStages()
    {
        var completedStage = Guid.NewGuid();
        var incompleteStage = Guid.NewGuid();
        var target = Target(new HashSet<Guid> { completedStage });
        var store = new Store();
        var scheduler = new Scheduler();
        var monitor = new MonitorPenetrationStages(
            new Targets(target),
            new Configurations([completedStage, incompleteStage]),
            new RecordSystemScoringEvent(store, scheduler, scheduler));

        var result = await monitor.ExecuteAsync();

        await Assert.That(result.CreatedCount).IsEqualTo(1);
        var command = store.Commands.Single();
        await Assert.That(command.Kind).IsEqualTo(ScoringEventKind.PenetrationStage);
        await Assert.That(command.Result).IsEqualTo(ScoringResult.PlatformFailed);
        await Assert.That(command.FailureCode).IsEqualTo(ScoringFailureCode.ProducerUnavailable);
        await Assert.That(command.StageId).IsEqualTo(incompleteStage);
        await Assert.That(command.ChallengeInstanceId).IsEqualTo(target.ChallengeInstanceId);
        await Assert.That(command.SourceKey)
            .IsEqualTo($"penetration:{target.ChallengeInstanceId:N}:stage:{incompleteStage:N}:runtime-failed");
    }

    [Test]
    public async Task AllStagesCompleted_RecordsNoPlatformFailure()
    {
        var stage = Guid.NewGuid();
        var store = new Store();
        var scheduler = new Scheduler();
        var monitor = new MonitorPenetrationStages(
            new Targets(Target(new HashSet<Guid> { stage })),
            new Configurations([stage]),
            new RecordSystemScoringEvent(store, scheduler, scheduler));

        var result = await monitor.ExecuteAsync();

        await Assert.That(result.TargetCount).IsEqualTo(1);
        await Assert.That(result.CreatedCount).IsEqualTo(0);
        await Assert.That(store.Commands).IsEmpty();
    }

    private static PenetrationStageMonitorTarget Target(IReadOnlySet<Guid> completed) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), FailedAt, "{}", completed);

    private sealed class Targets(PenetrationStageMonitorTarget target) : IPenetrationStageMonitorTargetStore
    {
        public Task<IReadOnlyList<PenetrationStageMonitorTarget>> ListFailedAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PenetrationStageMonitorTarget>>([target]);
    }

    private sealed class Configurations(IReadOnlyList<Guid> stageIds) : IPenetrationStageConfigurationCatalog
    {
        public PenetrationStageProgress GetProgress(
            string challengeConfigurationJson,
            IReadOnlySet<Guid> completedStageIds) =>
            new(stageIds.Count, stageIds.Where(stageId => !completedStageIds.Contains(stageId)).ToArray());
    }

    private sealed class Store : ISystemScoringEventStore
    {
        private readonly Dictionary<string, Guid> ids = [];
        public List<RecordSystemScoringEventCommand> Commands { get; } = [];
        public Task<CompetitionStatus?> GetCompetitionStatusAsync(
            Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionStatus?>(CompetitionStatus.Running);
        public Task<GameMode?> GetCompetitionModeAsync(
            Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult<GameMode?>(GameMode.Penetration);
        public Task<RecordSystemScoringEventResult> RecordAsync(
            RecordSystemScoringEventCommand command,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            if (ids.TryGetValue(command.SourceKey, out var id))
                return Task.FromResult(new RecordSystemScoringEventResult(id, false));
            id = Guid.NewGuid();
            ids[command.SourceKey] = id;
            return Task.FromResult(new RecordSystemScoringEventResult(id, true));
        }
    }

    private sealed class Scheduler : IBackgroundWorkScheduler, IBackgroundWorkAdmissionGate
    {
        public bool IsAccepting => true;
        public IBackgroundWorkAdmissionLease TryEnter() => new Lease();
        public ValueTask EnqueueSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EnqueueSystemEventAsync(Guid scoringEventId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EnqueueLeaderboardRefreshAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EnqueueCompetitionRebuildAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        private sealed class Lease : IBackgroundWorkAdmissionLease
        {
            public CancellationToken DrainCancellation => CancellationToken.None;
            public void Dispose() { }
        }
    }
}
