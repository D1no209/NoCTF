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
using NoCTF.Worker;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class AwdpFixResultPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Competition_revision_change_supersedes_inflight_fix(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_awdp_revision")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            await using (var mutation = new NoCtfDbContext(options))
                await mutation.Competitions
                    .Where(competition => competition.Id == fixture.CompetitionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        competition => competition.ConfigurationRevision,
                        competition => competition.ConfigurationRevision + 1),
                        cancellationToken);

            var result = AwdpFixResult.Create(
                fixture.SubmissionId,
                fixture.RuntimeId,
                generation: 1,
                processingVersion: 7,
                runtimeProcessingVersion: 3,
                AwdpFixOutcome.Fixed,
                fixture.Now);
            var outbox = new RecordingOutbox();
            InternalResultDisposition disposition;
            await using (var db = new NoCtfDbContext(options))
                disposition = await new EfInternalResultStore(db, outbox)
                    .RecordAwdpAsync(result, cancellationToken);

            await Assert.That(disposition).IsEqualTo(InternalResultDisposition.Superseded);
            await using var verify = new NoCtfDbContext(options);
            var submission = await verify.Submissions.SingleAsync(cancellationToken);
            var runtime = await verify.RuntimeInstances.SingleAsync(cancellationToken);
            await Assert.That(submission.EvaluationState)
                .IsEqualTo(SubmissionEvaluationState.PlatformFailed);
            await Assert.That(runtime.State).IsEqualTo(RuntimeState.Stopping);
            await Assert.That(await verify.ScoringEvents.AnyAsync(cancellationToken)).IsFalse();
        });
    }

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

            var expired = await AddPendingFixAsync(options, fixture, cancellationToken);
            await using (var wrongOwnerDb = new NoCtfDbContext(options))
                await BackendMessageHandlers.Handle(new ExpireAwdpFixVerification(
                    expired.SubmissionId,
                    expired.RuntimeId,
                    2,
                    8,
                    5,
                    fixture.Now,
                    "awdp",
                    "runner-b"), wrongOwnerDb, outbox, cancellationToken);
            await using (var wrongOwnerVerify = new NoCtfDbContext(options))
            {
                var pendingSubmission = await wrongOwnerVerify.Submissions.SingleAsync(
                    item => item.Id == expired.SubmissionId, cancellationToken);
                var runningRuntime = await wrongOwnerVerify.RuntimeInstances.SingleAsync(
                    item => item.Id == expired.RuntimeId, cancellationToken);
                await Assert.That(pendingSubmission.EvaluationState)
                    .IsEqualTo(SubmissionEvaluationState.Processing);
                await Assert.That(runningRuntime.State).IsEqualTo(RuntimeState.Running);
                await Assert.That(outbox.NodeMessages.OfType<StopContainerRuntime>().Count())
                    .IsEqualTo(1);
            }
            await using (var expireDb = new NoCtfDbContext(options))
                await BackendMessageHandlers.Handle(new ExpireAwdpFixVerification(
                    expired.SubmissionId,
                    expired.RuntimeId,
                    2,
                    8,
                    5,
                    fixture.Now,
                    "awdp",
                    "runner-a"), expireDb, outbox, cancellationToken);
            await using var expiredVerify = new NoCtfDbContext(options);
            var expiredSubmission = await expiredVerify.Submissions.SingleAsync(
                item => item.Id == expired.SubmissionId, cancellationToken);
            var expiredRuntime = await expiredVerify.RuntimeInstances.SingleAsync(
                item => item.Id == expired.RuntimeId, cancellationToken);
            await Assert.That(expiredSubmission.EvaluationState)
                .IsEqualTo(SubmissionEvaluationState.PlatformFailed);
            await Assert.That(expiredRuntime.State).IsEqualTo(RuntimeState.Stopping);
            await Assert.That(outbox.NodeMessages.OfType<StopContainerRuntime>().Count())
                .IsEqualTo(2);
        });
    }

    private static async Task<(Guid SubmissionId, Guid RuntimeId)> AddPendingFixAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var original = await db.Submissions.AsNoTracking()
            .SingleAsync(item => item.Id == fixture.SubmissionId, cancellationToken);
        var submissionId = Guid.NewGuid();
        var runtimeId = Guid.NewGuid();
        var patchUploadId = Guid.NewGuid();
        db.PatchUploads.Add(new PatchUpload
        {
            Id = patchUploadId,
            CompetitionId = original.CompetitionId,
            CompetitionChallengeId = original.CompetitionChallengeId,
            TeamId = original.TeamId,
            UploadedByUserId = original.SubmittedByUserId,
            ObjectKey = $"patches/{patchUploadId:N}",
            OriginalFileName = "fix.tar.gz",
            ContentType = "application/gzip",
            ByteLength = 1,
            Sha256 = new byte[32],
            UploadedAt = fixture.Now,
            ConsumedAt = fixture.Now,
            SubmissionId = submissionId
        });
        db.Submissions.Add(new Submission
        {
            Id = submissionId,
            CompetitionId = original.CompetitionId,
            TeamId = original.TeamId,
            CompetitionChallengeId = original.CompetitionChallengeId,
            SubmittedByUserId = original.SubmittedByUserId,
            Kind = SubmissionKind.Fix,
            ReceivedAt = fixture.Now,
            PatchUploadId = patchUploadId,
            EvaluationState = SubmissionEvaluationState.Processing,
            EvaluationUpdatedAt = fixture.Now,
            ProcessingVersion = 8
        });
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = runtimeId,
            CompetitionId = original.CompetitionId,
            CompetitionChallengeId = original.CompetitionChallengeId,
            Purpose = RuntimePurpose.AwdpTarget,
            SubmissionId = submissionId,
            SubmissionProcessingVersion = 8,
            Generation = 2,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerPool = "awdp",
            RunnerId = "runner-a",
            State = RuntimeState.Running,
            ProcessingVersion = 5,
            ConfigurationRevision = 2,
            CompetitionConfigurationRevision = 0,
            ProviderReceiptJson = "{}",
            CreatedAt = fixture.Now,
            RunningAt = fixture.Now
        });
        await db.SaveChangesAsync(cancellationToken);
        return (submissionId, runtimeId);
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
        var patchUploadId = Guid.NewGuid();
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
        db.PatchUploads.Add(new PatchUpload
        {
            Id = patchUploadId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            UploadedByUserId = memberId,
            ObjectKey = $"patches/{patchUploadId:N}",
            OriginalFileName = "fix.tar.gz",
            ContentType = "application/gzip",
            ByteLength = 1,
            Sha256 = new byte[32],
            UploadedAt = now,
            ConsumedAt = now,
            SubmissionId = submissionId
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
            PatchUploadId = patchUploadId,
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
            CompetitionConfigurationRevision = 0,
            ProviderReceiptJson = "{}",
            CreatedAt = now,
            RunningAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(now, competitionId, submissionId, runtimeId);
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
        Guid CompetitionId,
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
