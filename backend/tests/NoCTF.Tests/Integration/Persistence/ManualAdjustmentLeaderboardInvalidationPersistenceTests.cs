using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.GameplayFacts.Intake;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ManualAdjustmentLeaderboardInvalidationPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Accepted_adjustment_commits_a_scoring_event_that_invalidates_the_leaderboard(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_manual_adjustment_invalidation")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.Parse("2026-08-27T12:00:00Z");
            var actorId = Guid.CreateVersion7();
            var competitionId = Guid.CreateVersion7();
            var challengeId = Guid.CreateVersion7();
            var competitionChallengeId = Guid.CreateVersion7();
            var teamId = Guid.CreateVersion7();
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            db.Users.Add(new User
            {
                Id = actorId,
                UserName = "adjustment-judge",
                NormalizedUserName = "ADJUSTMENT-JUDGE",
                Email = "adjustment-judge@example.test",
                PasswordHash = "test",
                Kind = UserKind.Human,
                Role = UserRole.Organizer,
                EmailVerifiedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Competitions.Add(new CtfCompetition
            {
                Id = competitionId,
                OwnerId = actorId,
                Title = "Manual adjustment invalidation",
                ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
                FlagDerivationSecret = new byte[32],
                StartAt = now.AddHours(-1),
                EndAt = now.AddHours(1),
                Status = CompetitionStatus.Running,
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Challenges.Add(new CtfChallenge
            {
                Id = challengeId,
                OwnerId = actorId,
                Visibility = ChallengeVisibility.Private,
                Title = "Manual adjustment challenge",
                Direction = "PWN",
                Definition = TestConfigurations.Definition(GameMode.Ctf),
                CreatedAt = now,
                UpdatedAt = now
            });
            db.CompetitionChallenges.Add(new CtfCompetitionChallenge
            {
                Id = competitionChallengeId,
                CompetitionId = competitionId,
                ChallengeId = challengeId,
                IsPublished = true,
                Rules = TestConfigurations.Rules(GameMode.Ctf),
                UpdatedAt = now
            });
            db.Teams.Add(new Team
            {
                Id = teamId,
                CompetitionId = competitionId,
                Name = "Adjusted Team",
                CaptainId = actorId,
                MemberIds = [actorId],
                InvitationToken = Guid.CreateVersion7().ToString("N"),
                RegistrationStatus = TeamRegistrationStatus.Approved,
                RegisteredAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            var outbox = new RecordingOutbox();
            var eventStore = new CompetitionEventStore(db, outbox);
            var intake = new GameplayFactIntakeStore(db, outbox, eventStore);
            var factId = Guid.CreateVersion7(now);

            var result = await intake.TryAcceptManualAdjustmentAsync(new(
                factId,
                competitionId,
                teamId,
                competitionChallengeId,
                actorId,
                25,
                now), cancellationToken);

            await Assert.That(result.State).IsEqualTo(GameplayFactAcceptanceState.Created);
            var scoringEvent = await db.CompetitionEvents.AsNoTracking()
                .SingleAsync(@event => @event.Kind == CompetitionEventKind.ScoringRecorded,
                    cancellationToken);
            await Assert.That(scoringEvent.Visibility).IsEqualTo(CompetitionEventVisibility.Staff);
            await Assert.That(scoringEvent.GameplayFactId).IsEqualTo(factId);
            await Assert.That(scoringEvent.TeamId).IsEqualTo(teamId);
            await Assert.That(outbox.Messages.OfType<CompetitionEventCommitted>().Single().CompetitionId)
                .IsEqualTo(competitionId);
            await Assert.That(outbox.FlushCount).IsEqualTo(1);
        });
    }

    private sealed class RecordingOutbox : IPostCommitMessagePublisher
    {
        public ConcurrentQueue<object> Messages { get; } = [];
        public int FlushCount { get; private set; }

        public ValueTask PublishAsync<T>(T message)
        {
            Messages.Enqueue(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;

        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync()
        {
            FlushCount += 1;
            return Task.CompletedTask;
        }
    }
}
