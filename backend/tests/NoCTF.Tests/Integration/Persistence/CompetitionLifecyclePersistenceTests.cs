using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Persistence.UseCaseAdapters;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CompetitionLifecyclePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Awd_pause_and_resume_freeze_checkers_and_extend_the_current_flag_window(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_lifecycle")
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

            await using (var pauseDb = new NoCtfDbContext(options))
            {
                var store = new EfCompetitionLifecycleStore(pauseDb, null!, outbox);
                var applied = await store.TryTransitionWithAuditAsync(
                    fixture.CompetitionId,
                    CompetitionStatus.Running,
                    CompetitionStatus.Paused,
                    fixture.OwnerId,
                    "pause",
                    false,
                    CompetitionLifecycleEffects.ProjectLeaderboard,
                    cancellationToken);
                await Assert.That(applied).IsTrue();
            }

            var pausedAt = fixture.Now.AddMinutes(-5);
            await using (var adjustDb = new NoCtfDbContext(options))
            {
                var pauseAudit = await adjustDb.Set<CompetitionLifecycleAudit>()
                    .SingleAsync(audit => audit.CompetitionId == fixture.CompetitionId
                        && audit.To == CompetitionStatus.Paused, cancellationToken);
                pauseAudit.OccurredAt = pausedAt;
                await adjustDb.SaveChangesAsync(cancellationToken);
            }

            await using (var resumeDb = new NoCtfDbContext(options))
            {
                var store = new EfCompetitionLifecycleStore(resumeDb, null!, outbox);
                var applied = await store.TryTransitionWithAuditAsync(
                    fixture.CompetitionId,
                    CompetitionStatus.Paused,
                    CompetitionStatus.Running,
                    fixture.OwnerId,
                    "resume",
                    false,
                    CompetitionLifecycleEffects.ProjectLeaderboard
                        | CompetitionLifecycleEffects.ProvisionRuntimes,
                    cancellationToken);
                await Assert.That(applied).IsTrue();
            }

            await using var verify = new NoCtfDbContext(options);
            var competition = await verify.Competitions.AsNoTracking().SingleAsync(
                item => item.Id == fixture.CompetitionId,
                cancellationToken);
            await Assert.That(competition.Status).IsEqualTo(CompetitionStatus.Running);
            await Assert.That(competition.RunningSince).IsNotNull();
            await Assert.That(competition.AccumulatedRunningSeconds).IsGreaterThan(0);
            var runtime = await verify.RuntimeInstances.AsNoTracking().SingleAsync(
                item => item.Id == fixture.RuntimeId,
                cancellationToken);
            await Assert.That(runtime.NextCheckerDueAt).IsNotNull();
            await Assert.That(runtime.NextCheckerDueAt!.Value)
                .IsGreaterThanOrEqualTo(pausedAt.AddMinutes(5));
            await Assert.That(runtime.CheckerSequence).IsEqualTo(1);
            var flag = await verify.ChallengeFlags.AsNoTracking().SingleAsync(
                item => item.Id == fixture.FlagId,
                cancellationToken);
            await Assert.That(flag.ValidUntil).IsNotNull();
            await Assert.That(flag.ValidUntil!.Value)
                .IsGreaterThan(fixture.OriginalValidUntil.AddMinutes(4));
            await Assert.That(outbox.Published.OfType<ProjectLeaderboard>().Count()).IsEqualTo(2);
            await Assert.That(outbox.Published.OfType<ProvisionCompetitionRuntimes>().Count())
                .IsEqualTo(1);
        });
    }

    private static async Task<LifecycleFixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var runtimeId = Guid.CreateVersion7();
        var flagId = Guid.CreateVersion7();
        var originalValidUntil = now.AddMinutes(5);
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "owner",
            NormalizedUserName = "OWNER",
            Email = "owner@example.test",
            NormalizedEmail = "OWNER@EXAMPLE.TEST",
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "AWD lifecycle",
            OwnerId = ownerId,
            Mode = GameMode.Awd,
            Status = CompetitionStatus.Running,
            RunningSince = now.AddMinutes(-2),
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            FlagDerivationSecret = new byte[32],
            CreatedAt = now,
            UpdatedAt = now,
            ConfigurationUpdatedAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Title = "Service",
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
        db.RuntimeInstances.Add(new RuntimeInstance
        {
            Id = runtimeId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            Generation = 1,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            RunnerPool = "awd",
            State = RuntimeState.Running,
            ProcessingVersion = 1,
            ProviderReceiptJson = "{\"id\":\"runtime\"}",
            NextCheckerDueAt = now.AddMinutes(1),
            CreatedAt = now,
            RunningAt = now
        });
        db.ChallengeFlags.Add(new ChallengeFlag
        {
            Id = flagId,
            CompetitionChallengeId = competitionChallengeId,
            Flag = "flag{pause}",
            FlagSha256 = new byte[32],
            SpecificationKind = SpecificationKind.AwdRound,
            SpecificationId = AwdRoundSpecificationId.FromRound(1).Value,
            ValidStart = now.AddMinutes(-10),
            ValidUntil = originalValidUntil,
            CreatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(
            now,
            ownerId,
            competitionId,
            runtimeId,
            flagId,
            originalValidUntil);
    }

    private sealed record LifecycleFixture(
        DateTimeOffset Now,
        Guid OwnerId,
        Guid CompetitionId,
        Guid RuntimeId,
        Guid FlagId,
        DateTimeOffset OriginalValidUntil);

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> Published { get; } = [];
        public List<(object Message, DateTimeOffset At)> Scheduled { get; } = [];

        public ValueTask PublishAsync<T>(T message)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt)
        {
            Scheduled.Add((message!, scheduledAt));
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage =>
            throw new NotSupportedException();

        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => throw new NotSupportedException();

        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage =>
            throw new NotSupportedException();

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }

}
