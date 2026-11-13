using System.Text.Json;
using System.Text.Json.Serialization;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;
using NATS.Net;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Hosting;
using NoCTF.Infrastructure.Competitions.Awdp;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
public sealed class AwdpRoundSchedulingTests
{
    [Test, Timeout(300_000)]
    public async Task Round_boundaries_rebuild_from_lifecycle_facts_and_dispatch_through_JetStream(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Draft, ct);
        var now = DateTimeOffset.UtcNow;
        var id = Guid.CreateVersion7();
        var configuration = (AwdpCompetitionModeConfiguration)CompetitionModeConfigurationDefaults.Create(GameMode.Awdp, id);
        configuration.RoundDurationSeconds = 30;
        var competition = new AwdpCompetition
        {
            Id = id, OwnerId = fixture.OwnerId, Title = "AWDP boundary",
            Status = CompetitionStatus.Running, StartAt = now.AddSeconds(-70), EndAt = now.AddHours(1),
            ModeConfiguration = configuration
        };
        fixture.Db.Competitions.Add(competition);
        var events = new CompetitionEventStore(fixture.Db, fixture.Publisher);
        await events.RecordAsync(new(id, CompetitionEventKind.CompetitionLifecycleChanged,
            CompetitionEventLevel.Information, CompetitionEventVisibility.Public, competition.StartAt,
            PreviousCompetitionStatus: CompetitionStatus.Published, CompetitionStatus: CompetitionStatus.Running), ct);
        await fixture.Db.SaveChangesAsync(ct);
        var source = new AwdpRoundScheduleSource(fixture.Db);
        var entry = (await source.RebuildAsync(now, ct)).Single();
        await Assert.That(entry.DueAt).IsEqualTo(now.AddSeconds(20));
        await Assert.That(entry.Kind).IsEqualTo(ClusterScheduleKind.AwdpRound);
        await Assert.That(entry.Interval).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That((await new AwdpRoundScheduleSource(fixture.Db).RebuildAsync(now.AddSeconds(7), ct)).Single().DueAt)
            .IsEqualTo(entry.DueAt);

        competition.Status = CompetitionStatus.Paused;
        await events.RecordAsync(new(id, CompetitionEventKind.CompetitionLifecycleChanged,
            CompetitionEventLevel.Information, CompetitionEventVisibility.Public, now.AddSeconds(8),
            PreviousCompetitionStatus: CompetitionStatus.Running, CompetitionStatus: CompetitionStatus.Paused), ct);
        await fixture.Db.SaveChangesAsync(ct);
        await Assert.That(await source.RebuildAsync(now.AddSeconds(10), ct)).IsEmpty();
        competition.Status = CompetitionStatus.Running;
        await events.RecordAsync(new(id, CompetitionEventKind.CompetitionLifecycleChanged,
            CompetitionEventLevel.Information, CompetitionEventVisibility.Public, now.AddSeconds(18),
            PreviousCompetitionStatus: CompetitionStatus.Paused, CompetitionStatus: CompetitionStatus.Running), ct);
        await fixture.Db.SaveChangesAsync(ct);
        var resumed = (await source.RebuildAsync(now.AddSeconds(20), ct)).Single();
        await Assert.That(resumed.DueAt).IsEqualTo(now.AddSeconds(30));

        await using var connection = new NatsConnection(new NatsOpts { Url = fixture.NatsUrl });
        var consumer = await connection.CreateJetStreamContext().CreateOrUpdateConsumerAsync(NatsSubjects.ProjectionStream,
            new ConsumerConfig("awdp-round-boundaries")
            {
                FilterSubject = NatsSubjects.Subject(WorkerQueue.Projection), AckPolicy = ConsumerConfigAckPolicy.Explicit
            }, ct);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new JsonStringEnumConverter());
        foreach (var boundary in new[] { entry, resumed.At(resumed.DueAt.AddSeconds(30)) })
        {
            await MaintenanceTickAgent.PublishMessageAsync(fixture.MessageBus, boundary.Message);
            var message = await consumer.NextAsync<byte[]>(opts: new() { Expires = TimeSpan.FromSeconds(5) }, cancellationToken: ct);
            await Assert.That(message).IsNotNull();
            await Assert.That(JsonSerializer.Deserialize<ProjectLeaderboard>(message!.Data!, json))
                .IsEqualTo(new ProjectLeaderboard(id, LeaderboardRefreshMode.Force));
            await message.AckAsync(cancellationToken: ct);
        }
        competition.DeletedAt = now.AddSeconds(21);
        await fixture.Db.SaveChangesAsync(ct);
        await Assert.That(await source.RebuildAsync(now.AddSeconds(22), ct)).IsEmpty();
    }
}
