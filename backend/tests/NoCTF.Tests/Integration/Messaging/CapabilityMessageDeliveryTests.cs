using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;
using NoCTF.Application.Competitions.Awd.Fixes;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Hosting;
using NoCTF.Infrastructure.Competitions.Events;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
public sealed class CapabilityMessageDeliveryTests
{
    [Test, Timeout(300_000)]
    public async Task Committed_event_replay_keeps_each_destination_and_scheduled_recovery_reaches_its_queue(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Running, ct);
        await using var connection = new NatsConnection(new NatsOpts { Url = fixture.NatsUrl });
        var jetStream = connection.CreateJetStreamContext();
        var eventConsumer = await jetStream.CreateOrUpdateConsumerAsync(NatsSubjects.EventsStream,
            new ConsumerConfig("capability-event-replay") { FilterSubject = "noctf.v2.events.>", AckPolicy = ConsumerConfigAckPolicy.Explicit }, ct);
        var ordinary = NatsSubjects.Subject(WorkerQueue.Control);
        var runner = "capability-recovery";
        var node = NatsSubjects.Runner(RunnerNodeQueueName.FromRunnerId(runner).Value);
        var controlConsumer = await jetStream.CreateOrUpdateConsumerAsync(NatsSubjects.ControlStream,
            new ConsumerConfig("capability-control-recovery") { FilterSubject = ordinary, AckPolicy = ConsumerConfigAckPolicy.Explicit }, ct);
        var runnerConsumer = await jetStream.CreateOrUpdateConsumerAsync(NatsSubjects.RunnerStream,
            new ConsumerConfig("capability-node-recovery") { FilterSubject = node, AckPolicy = ConsumerConfigAckPolicy.Explicit }, ct);
        var eventId = await new CompetitionEventStore(fixture.Db, fixture.Publisher).RecordAsync(new(fixture.CompetitionId,
            CompetitionEventKind.CompetitionUpdated, CompetitionEventLevel.Information, CompetitionEventVisibility.Public, fixture.Now), ct);
        await fixture.Db.SaveChangesAsync(ct); await fixture.Publisher.FlushCommittedMessagesAsync();
        await fixture.Publisher.PublishAsync(new CompetitionEventCommitted(fixture.CompetitionId, eventId,
            CompetitionEventKind.CompetitionUpdated, CompetitionEventLevel.Information, fixture.Now));
        await fixture.Publisher.FlushCommittedMessagesAsync();
        await fixture.AssertPublishedEventsAsync(ct);
        var receivedEvents = new List<string>();
        for (var index = 0; index < 3; index++)
        {
            var message = await eventConsumer.NextAsync<byte[]>(opts: new() { Expires = TimeSpan.FromSeconds(5) }, cancellationToken: ct);
            await Assert.That(message).IsNotNull(); receivedEvents.Add(message!.Subject); await message.AckAsync(cancellationToken: ct);
        }
        await Assert.That(receivedEvents).IsEquivalentTo(new[] { NatsSubjects.RealtimeEvents, NatsSubjects.LeaderboardEvents, NatsSubjects.WebhookEvents });
        await Assert.That(await eventConsumer.NextAsync<byte[]>(opts: new() { Expires = TimeSpan.FromSeconds(1) }, cancellationToken: ct)).IsNull();

        var scheduledAt = DateTimeOffset.UtcNow.AddSeconds(3);
        await fixture.Publisher.ScheduleAsync(new StopRuntime(Guid.NewGuid()), scheduledAt);
        await fixture.Publisher.ScheduleAsync(new StopRuntime(Guid.NewGuid()), scheduledAt.AddSeconds(1));
        await fixture.Publisher.ScheduleToRunnerNodeAsync(new ObservePersistentFix(Guid.NewGuid(), Guid.NewGuid(), runner, 0), scheduledAt);
        await fixture.Publisher.FlushCommittedMessagesAsync();
        foreach (var consumer in new[] { controlConsumer, controlConsumer, runnerConsumer })
        {
            var message = await consumer.NextAsync<byte[]>(opts: new() { Expires = TimeSpan.FromSeconds(10) }, cancellationToken: ct);
            await Assert.That(message).IsNotNull();
            await Assert.That(DateTimeOffset.UtcNow >= scheduledAt).IsTrue();
            await message!.AckAsync(cancellationToken: ct);
        }
    }
}
