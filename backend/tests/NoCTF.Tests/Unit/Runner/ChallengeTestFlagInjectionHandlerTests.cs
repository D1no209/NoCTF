using Microsoft.Extensions.Options;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Unit.Runner;

public sealed class ChallengeTestFlagInjectionHandlerTests
{
    [Test]
    public async Task Handle_Success_MarksTheTestFlagInjected()
    {
        var store = new RecordingStore(CreateWork());
        var outbox = new RecordingOutbox();
        var handler = CreateHandler(store, new FixedExecutor(new(0, false)), outbox);
        var message = CreateMessage();

        var outcome = await handler.Handle(message, CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Applied);
        await Assert.That(store.Completions).IsEquivalentTo([RuntimeTestFlagState.Succeeded]);
        await Assert.That(outbox.Scheduled).IsEmpty();
    }

    [Test]
    public async Task Handle_FirstFailure_RetriesOnTheSameRunnerNode()
    {
        var store = new RecordingStore(CreateWork());
        var outbox = new RecordingOutbox();
        var now = DateTimeOffset.Parse("2026-09-04T12:00:00Z");
        var handler = CreateHandler(store, new FixedExecutor(new(1, false)), outbox, now);

        var outcome = await handler.Handle(CreateMessage(), CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.DeferredSchedule);
        await Assert.That(store.Completions).IsEmpty();
        await Assert.That(outbox.Scheduled).HasSingleItem();
        var scheduled = outbox.Scheduled[0];
        await Assert.That(scheduled.Message.FailedAttempts).IsEqualTo(1);
        await Assert.That(scheduled.At).IsEqualTo(now.AddSeconds(2));
    }

    [Test]
    public async Task Handle_FinalFailure_MarksTheTestFlagFailed()
    {
        var store = new RecordingStore(CreateWork());
        var outbox = new RecordingOutbox();
        var handler = CreateHandler(store, new FixedExecutor(new(1, false)), outbox);

        var outcome = await handler.Handle(
            CreateMessage() with { FailedAttempts = 2 },
            CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Applied);
        await Assert.That(store.Completions).IsEquivalentTo([RuntimeTestFlagState.Failed]);
        await Assert.That(outbox.Scheduled).IsEmpty();
    }

    [Test]
    public async Task Handle_SupersededWork_ConvergesAnyRemainingPendingFlag()
    {
        var store = new RecordingStore(null);
        var handler = CreateHandler(
            store,
            new FixedExecutor(new(0, false)),
            new RecordingOutbox());

        var outcome = await handler.Handle(CreateMessage(), CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Superseded);
        await Assert.That(store.Completions).IsEquivalentTo([RuntimeTestFlagState.Failed]);
    }

    private static ChallengeTestFlagInjectionHandler CreateHandler(
        IChallengeTestFlagInjectionStore store,
        IAwdFlagInjectionExecutor executor,
        RecordingOutbox outbox,
        DateTimeOffset? now = null) =>
        new(
            store,
            executor,
            outbox,
            Options.Create(new RunnerOptions { Pool = "pool-test", Id = "runner-test" }),
            new FixedTimeProvider(now ?? DateTimeOffset.Parse("2026-09-04T12:00:00Z")));

    private static InjectChallengeTestFlag CreateMessage() =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "runner-test");

    private static AwdFlagInjectionWork CreateWork() =>
        new(
            Guid.Empty,
            RuntimeKind.Container,
            RuntimeProvider.Docker,
            "{}",
            "flag{test}",
            "set-flag ${FLAG}",
            null,
            TimeSpan.FromSeconds(30));

    private sealed class RecordingStore(AwdFlagInjectionWork? work)
        : IChallengeTestFlagInjectionStore
    {
        public List<RuntimeTestFlagState> Completions { get; } = [];

        public Task<AwdFlagInjectionWork?> ReadAsync(
            InjectChallengeTestFlag message,
            CancellationToken cancellationToken) =>
            Task.FromResult(work);

        public Task CompleteAsync(
            InjectChallengeTestFlag message,
            RuntimeTestFlagState state,
            CancellationToken cancellationToken)
        {
            Completions.Add(state);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedExecutor(ContainerExecResult result) : IAwdFlagInjectionExecutor
    {
        public Task<ContainerExecResult> ExecuteAsync(
            AwdFlagInjectionWork work,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<(InjectChallengeTestFlag Message, DateTimeOffset At)> Scheduled { get; } = [];

        public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage
        {
            if (message is InjectChallengeTestFlag injection)
                Scheduled.Add((injection, scheduledAt));
            return ValueTask.CompletedTask;
        }
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
