using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Submissions.Processing;
using NoCTF.Tests.Fixtures;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class SubmissionProcessingLeaderboardRevisionPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_submissions_increment_the_shared_leaderboard_revision_atomically(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_submission_processing_revision")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var updateBarrier = new CompetitionLeaderboardUpdateBarrier();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(updateBarrier)
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var outbox = new RecordingOutbox();
            var evaluator = Substitute.For<ISubmissionEvaluator>();
            evaluator.Evaluate(Arg.Any<SubmissionProcessingContext>())
                .Returns(new ScoringEventDecision(
                    ScoringEventKind.SubmissionEvaluation,
                    ScoringResult.Correct,
                    null,
                    fixture.Now,
                    "submission-revision-test"));
            var evaluatorCatalog = Substitute.For<ISubmissionEvaluatorCatalog>();
            evaluatorCatalog.Get(GameMode.Ctf).Returns(evaluator);
            var admissionPolicy = Substitute.For<ISubmissionAdmissionModePolicy>();
            admissionPolicy.GetRules(
                    GameMode.Ctf,
                    Arg.Any<string>(),
                    Arg.Any<string>())
                .Returns(new SubmissionAdmissionRules(
                    AllowsFlag: true,
                    AllowsFix: false,
                    MaxFlagAttempts: null,
                    MaxFixAttempts: null));
            var placementPolicy = Substitute.For<IRuntimePlacementPolicy>();
            updateBarrier.Enable();

            await Task.WhenAll(fixture.SubmissionIds.Select(submissionId =>
                ProcessAsync(
                    options,
                    evaluatorCatalog,
                    admissionPolicy,
                    placementPolicy,
                    outbox,
                    submissionId,
                    cancellationToken)));

            await Assert.That(updateBarrier.Arrivals).IsEqualTo(2);
            await using var verify = new NoCtfDbContext(options);
            var submissions = await verify.Submissions.AsNoTracking()
                .OrderBy(submission => submission.Id)
                .ToListAsync(cancellationToken);
            await Assert.That(submissions).Count().IsEqualTo(2);
            await Assert.That(submissions.All(
                submission => submission.EvaluationState == SubmissionEvaluationState.Completed))
                .IsTrue();
            await Assert.That(submissions.All(
                submission => submission.CurrentScoringEventId is not null))
                .IsTrue();
            var facts = await verify.ScoringEvents.AsNoTracking()
                .Where(scoringEvent => scoringEvent.CompetitionId == fixture.CompetitionId)
                .ToListAsync(cancellationToken);
            await Assert.That(facts).Count().IsEqualTo(2);
            await Assert.That(facts.Select(scoringEvent => scoringEvent.SubmissionId).Distinct())
                .Count().IsEqualTo(2);
            await Assert.That(facts.All(
                scoringEvent => scoringEvent.Result == ScoringResult.Correct))
                .IsTrue();
            var leaderboardRevision = await verify.Competitions.AsNoTracking()
                .Where(competition => competition.Id == fixture.CompetitionId)
                .Select(competition => competition.LeaderboardRevision)
                .SingleAsync(cancellationToken);
            await Assert.That(leaderboardRevision).IsEqualTo(2);
            await Assert.That(outbox.Published.OfType<ProjectLeaderboard>().Count())
                .IsEqualTo(2);
        });
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var submissionIds = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        db.Users.Add(new User
        {
            Id = userId,
            UserName = "submission-processor",
            NormalizedUserName = "SUBMISSION-PROCESSOR",
            Email = "submission-processor@example.test",
            NormalizedEmail = "SUBMISSION-PROCESSOR@EXAMPLE.TEST",
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "Submission processing revision",
            OwnerId = userId,
            Mode = GameMode.Ctf,
            Status = CompetitionStatus.Running,
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            RunningSince = now.AddMinutes(-1),
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "submission-team",
            NormalizedName = "SUBMISSION-TEAM",
            CaptainId = userId,
            MemberIds = [userId],
            InvitationToken = new string('s', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = userId,
            Mode = GameMode.Ctf,
            Title = "Submission challenge",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            IsPublished = true,
            UpdatedAt = now
        });
        db.Submissions.AddRange(submissionIds.Select((submissionId, index) => new Submission
        {
            Id = submissionId,
            CompetitionId = competitionId,
            TeamId = teamId,
            CompetitionChallengeId = competitionChallengeId,
            SubmittedByUserId = userId,
            Kind = SubmissionKind.Flag,
            SubmittedFlag = $"flag-{index}",
            SubmittedFlagSha256 = new byte[32],
            ReceivedAt = now.AddMilliseconds(index),
            EvaluationState = SubmissionEvaluationState.Queued,
            EvaluationUpdatedAt = now
        }));
        await db.SaveChangesAsync(cancellationToken);
        return new(now, competitionId, submissionIds);
    }

    private static async Task ProcessAsync(
        DbContextOptions<NoCtfDbContext> options,
        ISubmissionEvaluatorCatalog evaluatorCatalog,
        ISubmissionAdmissionModePolicy admissionPolicy,
        IRuntimePlacementPolicy placementPolicy,
        ITransactionalMessageOutbox outbox,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var processor = new SubmissionProcessor(
            db,
            evaluatorCatalog,
            admissionPolicy,
            placementPolicy,
            outbox);
        await processor.ProcessAsync(submissionId, 0, cancellationToken);
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public ConcurrentQueue<object> Published { get; } = new();

        public ValueTask PublishAsync<T>(T message)
        {
            Published.Enqueue(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt)
        {
            Published.Enqueue(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishToRunnerPoolAsync<T>(T message)
            where T : IRunnerPoolMessage =>
            ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerPoolAsync<T>(
            T message,
            DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage =>
            ValueTask.CompletedTask;

        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage =>
            ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerNodeAsync<T>(
            T message,
            DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage =>
            ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid CompetitionId,
        IReadOnlyList<Guid> SubmissionIds);
}
