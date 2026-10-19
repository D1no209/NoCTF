using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Worker;
using Testcontainers.PostgreSql;
using DotNet.Testcontainers.Builders;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;
using NATS.Net;
using NoCTF.Hosting;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using Microsoft.Extensions.Logging.Abstractions;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
[NotInParallel]
public sealed class PendingGameplayFactDispatchTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Old_queued_and_processing_flag_facts_are_redispatched_but_recent_and_external_facts_are_not(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .Build();
            await postgres.StartAsync(cancellationToken);
            await using var nats = new ContainerBuilder(
                    "docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, assignRandomHostPort: true).WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
            await nats.StartAsync(cancellationToken);
            await using var connection = new NatsConnection(new NatsOpts {
                Url = $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}" });
            var jetstream = connection.CreateJetStreamContext();
            await jetstream.CreateStreamAsync(new StreamConfig(NatsSubjects.GameplayStream,
                [NatsSubjects.Subject(WorkerQueue.Gameplay)]), cancellationToken);
            await jetstream.CreateOrUpdateConsumerAsync(NatsSubjects.GameplayStream,
                new ConsumerConfig(WorkerQueueNames.Gameplay) { AckPolicy = ConsumerConfigAckPolicy.Explicit }, cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var userId = Guid.CreateVersion7(now);
            var competitionId = Guid.CreateVersion7(now);
            var templateId = Guid.CreateVersion7(now);
            var challengeId = Guid.CreateVersion7(now);
            var teamId = Guid.CreateVersion7(now);
            var queuedId = Guid.CreateVersion7(now);
            var processingId = Guid.CreateVersion7(now);
            var recentId = Guid.CreateVersion7(now);
            var externalId = Guid.CreateVersion7(now);

            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync(cancellationToken);
                setup.Users.Add(new User
                {
                    Id = userId, UserName = $"recovery-{userId:N}",
                    NormalizedUserName = $"RECOVERY-{userId:N}",
                    Email = $"{userId:N}@example.test", PasswordHash = "test",
                    CreatedAt = now, UpdatedAt = now
                });
                setup.Competitions.Add(new AwdCompetition
                {
                    Id = competitionId, OwnerId = userId, Title = "Recovery",
                    Status = CompetitionStatus.Running,
                    ModeConfiguration = TestConfigurations.Competition(GameMode.Awd),
                    StartAt = now.AddMinutes(-5), EndAt = now.AddHours(1),
                    FlagDerivationSecret = RandomNumberGenerator.GetBytes(32),
                    CreatedAt = now, UpdatedAt = now
                });
                setup.Challenges.Add(new AwdChallenge
                {
                    Id = templateId, OwnerId = userId, Title = "Recovery challenge",
                    Definition = TestConfigurations.Definition(GameMode.Awd),
                    CreatedAt = now, UpdatedAt = now
                });
                setup.CompetitionChallenges.Add(new AwdCompetitionChallenge
                {
                    Id = challengeId, CompetitionId = competitionId, ChallengeId = templateId,
                    IsPublished = true, Rules = TestConfigurations.Rules(GameMode.Awd),
                    UpdatedAt = now
                });
                setup.Teams.Add(new Team
                {
                    Id = teamId, CompetitionId = competitionId, Name = "Recovery team",
                    CaptainId = userId, MemberIds = [userId],
                    InvitationToken = Guid.NewGuid().ToString("N"),
                    RegistrationStatus = TeamRegistrationStatus.Approved,
                    RegisteredAt = now
                });
                FlagAttemptGameplayFact Flag(Guid id, GameplayFactState state, DateTimeOffset updatedAt) => new()
                {
                    Id = id, CompetitionId = competitionId, CompetitionChallengeId = challengeId,
                    TeamId = teamId, ActorUserId = userId, Value = "flag{recovery}",
                    ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes("flag{recovery}")),
                    State = state, OccurredAt = updatedAt, UpdatedAt = updatedAt
                };
                setup.GameplayFacts.AddRange(
                    Flag(queuedId, GameplayFactState.Queued, now.AddMinutes(-2)),
                    Flag(processingId, GameplayFactState.Processing, now.AddMinutes(-2)),
                    Flag(recentId, GameplayFactState.Queued, now));
                setup.GameplayFacts.Add(new AwdServiceTransitionGameplayFact
                {
                    Id = externalId, CompetitionId = competitionId,
                    CompetitionChallengeId = challengeId, TeamId = teamId,
                    State = GameplayFactState.Processing,
                    OccurredAt = now.AddMinutes(-2), UpdatedAt = now.AddMinutes(-2)
                });
                await setup.SaveChangesAsync(cancellationToken);
            }

            await using var db = new NoCtfDbContext(options);
            var publisher = new RecordingPublisher();
            var cursors = new GameplayFactRecoveryCursorStore(connection);
            var handler = new PendingGameplayFactDispatchHandler(db, publisher, TimeProvider.System,
                connection, cursors, NullLogger<PendingGameplayFactDispatchHandler>.Instance);
            await handler
                .Handle(new DispatchPendingGameplayFacts(now), cancellationToken);
            var dispatched = publisher.Messages.OfType<EvaluateGameplayFact>()
                .Select(message => message.GameplayFactId).ToArray();
            await Assert.That(dispatched).IsEquivalentTo([queuedId, processingId]);
            await Assert.That(publisher.Messages.OfType<EvaluateGameplayFact>()
                .All(message => message.ExpectedConcurrencyStamp != Guid.Empty
                    && message.Purpose == GameplayFactDispatchPurpose.Recovery
                    && message.RecoveryWindow > 0)).IsTrue();
            await Assert.That(publisher.FlushCount).IsEqualTo(1);
            var saved = await cursors.ReadAsync(cancellationToken);
            await Assert.That(saved.Cursor).IsNotNull();
            publisher.Messages.Clear();
            await handler.Handle(new DispatchPendingGameplayFacts(now), cancellationToken);
            await Assert.That(publisher.Messages.OfType<EvaluateGameplayFact>().Select(item => item.GameplayFactId))
                .IsEquivalentTo([queuedId, processingId]);
            var laterIds = Enumerable.Range(0, 65).Select(_ => Guid.NewGuid()).ToArray();
            db.GameplayFacts.AddRange(laterIds.Select(id => new FlagAttemptGameplayFact {
                Id = id, CompetitionId = competitionId, CompetitionChallengeId = challengeId,
                TeamId = teamId, ActorUserId = userId, Value = "flag{recovery}",
                ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes("flag{recovery}")),
                State = GameplayFactState.Queued, OccurredAt = now.AddMinutes(-1), UpdatedAt = now.AddMinutes(-1) }));
            await db.SaveChangesAsync(cancellationToken);
            var recovered = new HashSet<Guid>();
            for (var pass = 0; pass < 3; pass++)
            {
                publisher.Messages.Clear();
                await handler.Handle(new DispatchPendingGameplayFacts(now), cancellationToken);
                await Assert.That(publisher.Messages.Count <= PendingGameplayFactDispatchHandler.BatchSize).IsTrue();
                recovered.UnionWith(publisher.Messages.OfType<EvaluateGameplayFact>().Select(item => item.GameplayFactId));
            }
            await Assert.That(laterIds.All(recovered.Contains)).IsTrue();
            saved = await cursors.ReadAsync(cancellationToken);
            for (var index = 0; index < 17; index++)
                await jetstream.PublishAsync(NatsSubjects.Subject(WorkerQueue.Gameplay), index, cancellationToken: cancellationToken);
            publisher.Messages.Clear();
            await handler.Handle(new DispatchPendingGameplayFacts(now), cancellationToken);
            await Assert.That(publisher.Messages.Count).IsEqualTo(0);
            await Assert.That((await cursors.ReadAsync(cancellationToken)).Cursor).IsEqualTo(saved.Cursor);
            await jetstream.DeleteConsumerAsync(NatsSubjects.GameplayStream, WorkerQueueNames.Gameplay, cancellationToken);
            await handler.Handle(new DispatchPendingGameplayFacts(now), cancellationToken);
            await Assert.That(publisher.Messages.Count).IsEqualTo(0);
        });
    }

    private sealed class RecordingPublisher : IPostCommitMessagePublisher
    {
        public List<object> Messages { get; } = [];
        public int FlushCount { get; private set; }
        public ValueTask PublishAsync<T>(T message)
        {
            Messages.Add(message!);
            return ValueTask.CompletedTask;
        }
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            throw new NotSupportedException();
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage =>
            throw new NotSupportedException();
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => throw new NotSupportedException();
        public Task FlushOutgoingMessagesAsync() => FlushCommittedMessagesAsync();
        public Task FlushCommittedMessagesAsync()
        {
            FlushCount++;
            return Task.CompletedTask;
        }
    }
}
