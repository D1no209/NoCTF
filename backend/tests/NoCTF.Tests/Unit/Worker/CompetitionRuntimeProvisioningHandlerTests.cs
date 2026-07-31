using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Worker;

namespace NoCTF.Tests.Unit.Worker;

public sealed class CompetitionRuntimeProvisioningHandlerTests
{
    [Test]
    public async Task Awd_deferred_cleanup_schedules_the_same_message()
    {
        var message = new ProvisionCompetitionRuntimes(Guid.CreateVersion7());
        var awd = new FixedAwdProvisioner(AwdRuntimeProvisioningOutcome.DeferredCleanup);
        var koh = new FixedKohProvisioner(KohRuntimeProvisioningOutcome.RejectedBusiness);
        var outbox = new RecordingOutbox();
        await using var db = CreateUnusedDbContext();
        var before = DateTimeOffset.UtcNow;

        await BackendMessageHandlers.Handle(
            message,
            awd,
            koh,
            db,
            outbox,
            CancellationToken.None);

        var after = DateTimeOffset.UtcNow;
        var scheduled = outbox.Scheduled.Single();
        await Assert.That(scheduled.Message).IsEqualTo(message);
        await Assert.That(scheduled.At).IsGreaterThanOrEqualTo(before.AddSeconds(5));
        await Assert.That(scheduled.At).IsLessThanOrEqualTo(after.AddSeconds(5));
        await Assert.That(outbox.FlushCount).IsEqualTo(1);
        await Assert.That(koh.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task Koh_deferred_cleanup_schedules_the_same_message()
    {
        var message = new ProvisionCompetitionRuntimes(Guid.CreateVersion7());
        var awd = new FixedAwdProvisioner(AwdRuntimeProvisioningOutcome.NotApplicable);
        var koh = new FixedKohProvisioner(KohRuntimeProvisioningOutcome.DeferredCleanup);
        var outbox = new RecordingOutbox();
        await using var db = CreateUnusedDbContext();
        var before = DateTimeOffset.UtcNow;

        await BackendMessageHandlers.Handle(
            message,
            awd,
            koh,
            db,
            outbox,
            CancellationToken.None);

        var after = DateTimeOffset.UtcNow;
        var scheduled = outbox.Scheduled.Single();
        await Assert.That(scheduled.Message).IsEqualTo(message);
        await Assert.That(scheduled.At).IsGreaterThanOrEqualTo(before.AddSeconds(5));
        await Assert.That(scheduled.At).IsLessThanOrEqualTo(after.AddSeconds(5));
        await Assert.That(outbox.FlushCount).IsEqualTo(1);
        await Assert.That(awd.CallCount).IsEqualTo(1);
        await Assert.That(koh.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task Rejected_outcome_does_not_schedule_another_attempt()
    {
        var awd = new FixedAwdProvisioner(AwdRuntimeProvisioningOutcome.RejectedBusiness);
        var koh = new FixedKohProvisioner(KohRuntimeProvisioningOutcome.NotApplicable);
        var outbox = new RecordingOutbox();
        await using var db = CreateUnusedDbContext();

        await BackendMessageHandlers.Handle(
            new ProvisionCompetitionRuntimes(Guid.CreateVersion7()),
            awd,
            koh,
            db,
            outbox,
            CancellationToken.None);

        await Assert.That(outbox.Scheduled).IsEmpty();
        await Assert.That(outbox.FlushCount).IsEqualTo(0);
        await Assert.That(koh.CallCount).IsEqualTo(0);
    }

    private static NoCtfDbContext CreateUnusedDbContext() =>
        new(new DbContextOptionsBuilder<NoCtfDbContext>().Options);

    private sealed class FixedAwdProvisioner(AwdRuntimeProvisioningOutcome outcome)
        : IAwdRuntimeProvisioner
    {
        public int CallCount { get; private set; }

        public Task<AwdRuntimeProvisioningOutcome> EnsureAsync(
            Guid competitionId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(outcome);
        }
    }

    private sealed class FixedKohProvisioner(KohRuntimeProvisioningOutcome outcome)
        : IKohRuntimeProvisioner
    {
        public int CallCount { get; private set; }

        public Task<KohRuntimeProvisioningOutcome> EnsureAsync(
            Guid competitionId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(outcome);
        }
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<(object Message, DateTimeOffset At)> Scheduled { get; } = [];
        public int FlushCount { get; private set; }

        public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt)
        {
            Scheduled.Add((message!, scheduledAt));
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishToRunnerPoolAsync<T>(T message)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;

        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync()
        {
            FlushCount++;
            return Task.CompletedTask;
        }
    }
}
