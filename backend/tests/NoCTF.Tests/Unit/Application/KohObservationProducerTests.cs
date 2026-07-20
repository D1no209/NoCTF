using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Application.SystemProducers;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Competitions;

namespace NoCTF.Tests.Unit.Application;

public class KohObservationProducerTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ExecuteAsync_KnownController_RecordsCorrectStableIntervalFact()
    {
        var teamId = Guid.NewGuid();
        var fixture = new Fixture(new("blue", true), new Dictionary<string, Guid> { ["blue"] = teamId });

        var result = await fixture.Producer.ExecuteAsync(Start.AddSeconds(12));

        await Assert.That(result.CreatedCount).IsEqualTo(1);
        await Assert.That(fixture.Store.Commands).HasSingleItem();
        var command = fixture.Store.Commands[0];
        await Assert.That(command.TeamId).IsEqualTo(teamId);
        await Assert.That(command.Result).IsEqualTo(ScoringResult.Correct);
        await Assert.That(command.Kind).IsEqualTo(ScoringEventKind.KohObservation);
        await Assert.That(command.SourceKey).EndsWith("interval:2");
        await Assert.That(command.OccurredAt).IsEqualTo(Start.AddSeconds(10));
    }

    [Test]
    public async Task ExecuteAsync_UnknownController_RecordsRejectedFactWithoutTeam()
    {
        var fixture = new Fixture(new("unknown", true), new Dictionary<string, Guid>());

        await fixture.Producer.ExecuteAsync(Start.AddSeconds(5));

        var command = fixture.Store.Commands.Single();
        await Assert.That(command.TeamId).IsNull();
        await Assert.That(command.Result).IsEqualTo(ScoringResult.Rejected);
        await Assert.That(command.FailureCode).IsEqualTo(ScoringFailureCode.UnknownTeamIdentifier);
    }

    [Test]
    public async Task ExecuteAsync_BlankController_RecordsInvalidObservation()
    {
        var fixture = new Fixture(new(" ", true), new Dictionary<string, Guid>());

        await fixture.Producer.ExecuteAsync(Start.AddSeconds(5));

        var command = fixture.Store.Commands.Single();
        await Assert.That(command.Result).IsEqualTo(ScoringResult.Rejected);
        await Assert.That(command.FailureCode).IsEqualTo(ScoringFailureCode.InvalidObservation);
    }

    [Test]
    public async Task ExecuteAsync_InvalidTargetConfiguration_DoesNotStarveLaterTarget()
    {
        var store = new Store();
        var scheduler = new Scheduler();
        var record = new RecordSystemScoringEvent(store, scheduler, scheduler);
        var producer = new ProduceKohObservations(
            new TwoTargets(),
            new ThrowingConfigurations(),
            new Agent(new KohAgentObservation(null, true), false),
            record);

        var result = await producer.ExecuteAsync(Start.AddSeconds(5));

        await Assert.That(result.TargetCount).IsEqualTo(2);
        await Assert.That(result.FailedCount).IsEqualTo(1);
        await Assert.That(result.CreatedCount).IsEqualTo(1);
        await Assert.That(store.Commands).HasSingleItem();
    }

    [Test]
    public async Task ExecuteAsync_Timeout_RecordsPlatformFailedFact()
    {
        var fixture = new Fixture(new(null, false), new Dictionary<string, Guid>(), timeout: true);

        await fixture.Producer.ExecuteAsync(Start.AddSeconds(5));

        var command = fixture.Store.Commands.Single();
        await Assert.That(command.Result).IsEqualTo(ScoringResult.PlatformFailed);
        await Assert.That(command.FailureCode).IsEqualTo(ScoringFailureCode.ProducerTimeout);
    }

    [Test]
    public async Task ExecuteAsync_SameIntervalTwice_CreatesSingleFact()
    {
        var fixture = new Fixture(new(null, true), new Dictionary<string, Guid>());

        var first = await fixture.Producer.ExecuteAsync(Start.AddSeconds(6));
        var second = await fixture.Producer.ExecuteAsync(Start.AddSeconds(9));

        await Assert.That(first.CreatedCount).IsEqualTo(1);
        await Assert.That(second.CreatedCount).IsEqualTo(0);
        await Assert.That(fixture.Store.Commands.Select(command => command.SourceKey).Distinct()).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments(0, 0L)]
    [Arguments(4, 0L)]
    [Arguments(5, 1L)]
    [Arguments(12, 2L)]
    public async Task RoundClock_UsesZeroBasedStableIntervals(int seconds, long expected)
    {
        await Assert.That(CompetitionRoundClock.Interval(Start, Start.AddSeconds(seconds), 5))
            .IsEqualTo(expected);
    }

    private sealed class Fixture
    {
        public Fixture(
            KohAgentObservation observation,
            IReadOnlyDictionary<string, Guid> identifiers,
            bool timeout = false)
        {
            Store = new Store();
            var scheduler = new Scheduler();
            var record = new RecordSystemScoringEvent(Store, scheduler, scheduler);
            Producer = new(
                new Targets(),
                new Configurations(identifiers),
                new Agent(observation, timeout),
                record);
        }

        public Store Store { get; }
        public ProduceKohObservations Producer { get; }
    }

    private sealed class Targets : IKohProducerTargetStore
    {
        private readonly KohProducerTarget target = new(Guid.NewGuid(), Guid.NewGuid(), Start, "{}", "{}");

        public Task<IReadOnlyList<KohProducerTarget>> ListRunningAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KohProducerTarget>>([target]);
    }

    private sealed class Configurations(IReadOnlyDictionary<string, Guid> identifiers)
        : IKohProducerConfigurationCatalog
    {
        public KohProducerSettings Get(string competitionConfigurationJson, string challengeConfigurationJson) =>
            new(5, new Uri("http://agent.invalid/observe"), identifiers);
    }

    private sealed class TwoTargets : IKohProducerTargetStore
    {
        public Task<IReadOnlyList<KohProducerTarget>> ListRunningAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KohProducerTarget>>([
                new(Guid.NewGuid(), Guid.NewGuid(), Start, "invalid", "{}"),
                new(Guid.NewGuid(), Guid.NewGuid(), Start, "valid", "{}")]);
    }

    private sealed class ThrowingConfigurations : IKohProducerConfigurationCatalog
    {
        public KohProducerSettings Get(string competitionConfigurationJson, string challengeConfigurationJson) =>
            competitionConfigurationJson == "invalid"
                ? throw new FormatException("Invalid test configuration.")
                : new(5, new Uri("http://agent.invalid/observe"), new Dictionary<string, Guid>());
    }

    private sealed class Agent(KohAgentObservation observation, bool timeout) : IKohAgentClient
    {
        public Task<KohAgentObservation> ObserveAsync(
            Uri agentUri,
            TimeSpan timeoutValue,
            CancellationToken cancellationToken) =>
            timeout
                ? Task.FromException<KohAgentObservation>(new TimeoutException())
                : Task.FromResult(observation);
    }

    private sealed class Store : ISystemScoringEventStore
    {
        public Task<GameMode?> GetCompetitionModeAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult<GameMode?>(GameMode.Koh);
        private readonly Dictionary<string, Guid> ids = [];
        public List<RecordSystemScoringEventCommand> Commands { get; } = [];
        public Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken cancellationToken) =>
            Task.FromResult<CompetitionStatus?>(CompetitionStatus.Running);

        public Task<RecordSystemScoringEventResult> RecordAsync(
            RecordSystemScoringEventCommand command,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            if (ids.TryGetValue(command.SourceKey, out var existing))
                return Task.FromResult(new RecordSystemScoringEventResult(existing, false));
            var id = Guid.NewGuid();
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
