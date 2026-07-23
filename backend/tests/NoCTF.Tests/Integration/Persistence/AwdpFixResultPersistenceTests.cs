using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Persistence.UseCaseAdapters;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class AwdpFixResultPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Typed_result_is_idempotent_and_schedules_target_cleanup(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_awdp_result")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var outbox = new RecordingOutbox();
            var result = AwdpFixResult.Create(
                fixture.SubmissionId,
                fixture.RuntimeId,
                generation: 1,
                processingVersion: 7,
                runtimeProcessingVersion: 3,
                AwdpFixOutcome.Fixed,
                fixture.Now);

            InternalResultDisposition first;
            await using (var db = new NoCtfDbContext(options))
                first = await new EfInternalResultStore(db, outbox)
                    .RecordAwdpAsync(result, cancellationToken);
            InternalResultDisposition replay;
            await using (var db = new NoCtfDbContext(options))
                replay = await new EfInternalResultStore(db, outbox)
                    .RecordAwdpAsync(result, cancellationToken);

            await Assert.That(first).IsEqualTo(InternalResultDisposition.Applied);
            await Assert.That(replay).IsEqualTo(InternalResultDisposition.Duplicate);
            await Assert.That(outbox.NodeMessages.OfType<StopContainerRuntime>().Count())
                .IsEqualTo(1);
            await using var verify = new NoCtfDbContext(options);
            var submission = await verify.Submissions.SingleAsync(cancellationToken);
            var runtime = await verify.RuntimeInstances.SingleAsync(cancellationToken);
            var scoring = await verify.ScoringEvents.SingleAsync(cancellationToken);
            await Assert.That(submission.EvaluationState)
                .IsEqualTo(SubmissionEvaluationState.Completed);
            await Assert.That(scoring.Result).IsEqualTo(ScoringResult.Correct);
            await Assert.That(runtime.State).IsEqualTo(RuntimeState.Stopping);
            await Assert.That(runtime.ProcessingVersion).IsEqualTo(4);
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.Parse("2026-07-24T00:00:00Z");
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var competitionChallengeId = Guid.NewGuid();
        var submissionId = Guid.NewGuid();
        var runtimeId = Guid.NewGuid();
        db.Users.AddRange(
            NewUser(ownerId, "owner", now),
            NewUser(memberId, "member", now));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "AWDP result",
            OwnerId = ownerId,
            Mode = GameMode.Awdp,
            Status = CompetitionStatus.Running,
            ConfigurationJson = "{}",
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            RunningSince = now.AddMinutes(-1),
            FlagDerivationSecret = new byte[32],
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "team",
            NormalizedName = "TEAM",
            CaptainId = memberId,
            MemberIds = [memberId],
            InvitationToken = new string('a', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Title = "service",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            Revision = 2,
            ConfigurationJson = "{}",
            UpdatedAt = now
        });
        db.Submissions.Add(new Submission
        {
            Id = submissionId,
            CompetitionId = competitionId,
            TeamId = teamId,
            CompetitionChallengeId = competitionChallengeId,
            SubmittedByUserId = memberId,
            Kind = SubmissionKind.Fix,
            ReceivedAt = now,
            EvaluationState = SubmissionEvaluationState.Processing,
            EvaluationUpdatedAt = now,
            ProcessingVersion = 7
        });
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = runtimeId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            Purpose = RuntimePurpose.AwdpTarget,
            SubmissionId = submissionId,
            SubmissionProcessingVersion = 7,
            Generation = 1,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerPool = "awdp",
            RunnerId = "runner-a",
            State = RuntimeState.Running,
            ProcessingVersion = 3,
            ConfigurationRevision = 2,
            ProviderReceiptJson = "{}",
            CreatedAt = now,
            RunningAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(now, submissionId, runtimeId);
    }

    private static User NewUser(Guid id, string name, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        NormalizedEmail = $"{name.ToUpperInvariant()}@EXAMPLE.TEST",
        PasswordHash = "test",
        CreatedAt = now,
        UpdatedAt = now
    };

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid SubmissionId,
        Guid RuntimeId);

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> NodeMessages { get; } = [];
        public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;
        public ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage =>
            ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage
        {
            NodeMessages.Add(message!);
            return ValueTask.CompletedTask;
        }
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
