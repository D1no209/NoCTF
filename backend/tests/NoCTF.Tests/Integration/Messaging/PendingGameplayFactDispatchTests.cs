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
            await new PendingGameplayFactDispatchHandler(db, publisher, TimeProvider.System)
                .Handle(new DispatchPendingGameplayFacts(now), cancellationToken);
            var dispatched = publisher.Messages.OfType<EvaluateGameplayFact>()
                .Select(message => message.GameplayFactId).ToArray();
            await Assert.That(dispatched).IsEquivalentTo([queuedId, processingId]);
            await Assert.That(publisher.Messages.OfType<EvaluateGameplayFact>()
                .All(message => message.DispatchAttemptId != Guid.Empty)).IsTrue();
            await Assert.That(publisher.FlushCount).IsEqualTo(1);
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
