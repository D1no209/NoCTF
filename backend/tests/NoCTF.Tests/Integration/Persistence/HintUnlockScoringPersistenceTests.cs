using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Leaderboard;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using NoCTF.Infrastructure.Submissions.Processing;
using NoCTF.Worker;
using Testcontainers.PostgreSql;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("ChallengeHintUnlock")]
public sealed class HintUnlockScoringPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_unlocks_use_one_authoritative_team_score(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(
                "noctf_hint_unlock_concurrency",
                cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedScopeAsync(
                options,
                GameMode.Ctf,
                [60, 60],
                cancellationToken);
            var outbox = new RecordingOutbox();

            await Task.WhenAll(fixture.HintSubmissionIds.Select(submissionId =>
                ProcessAsync(options, outbox, submissionId, 0, cancellationToken)));

            await using var verify = new NoCtfDbContext(options);
            var outcomes = await verify.Submissions.AsNoTracking()
                .Where(submission => fixture.HintSubmissionIds.Contains(submission.Id))
                .Join(
                    verify.ScoringEvents.AsNoTracking(),
                    submission => submission.CurrentScoringEventId,
                    scoringEvent => (Guid?)scoringEvent.Id,
                    (_, scoringEvent) => new
                    {
                        scoringEvent.Result,
                        scoringEvent.FailureCode
                    })
                .ToListAsync(cancellationToken);
            await Assert.That(outcomes.Count(item => item.Result == ScoringResult.Correct))
                .IsEqualTo(1);
            await Assert.That(outcomes.Count(item =>
                    item.Result == ScoringResult.Rejected
                    && item.FailureCode == ScoringFailureCode.InsufficientScore))
                .IsEqualTo(1);
            await Assert.That(await ScoreAsync(
                    verify,
                    fixture.CompetitionId,
                    fixture.TeamId,
                    fixture.Now.AddMinutes(1),
                    cancellationToken))
                .IsEqualTo(40);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Rejudge_uses_the_live_projector_for_all_four_modes(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(
                "noctf_hint_unlock_rejudge",
                cancellationToken);
            var options = Options(postgres);
            var fixtures = new List<ScopeFixture>();
            foreach (var mode in Enum.GetValues<GameMode>())
            {
                fixtures.Add(await SeedScopeAsync(
                    options,
                    mode,
                    [40],
                    cancellationToken));
            }
            var outbox = new RecordingOutbox();

            foreach (var fixture in fixtures)
            {
                var submissionId = fixture.HintSubmissionIds.Single();
                await ProcessAsync(options, outbox, submissionId, 0, cancellationToken);
                await AssertCurrentAsync(
                    options,
                    submissionId,
                    ScoringResult.Correct,
                    failureCode: null,
                    cancellationToken);

                await using (var update = new NoCtfDbContext(options))
                {
                    var challenge = await update.CompetitionChallenges.SingleAsync(
                        item => item.Id == fixture.CompetitionChallengeId,
                        cancellationToken);
                    challenge.Hints.Single().Cost = 60;
                    challenge.Revision = checked(challenge.Revision + 1);
                    challenge.UpdatedAt = fixture.Now.AddMinutes(1);
                    await update.SaveChangesAsync(cancellationToken);
                }

                var beforeRejudge = outbox.Published.Count;
                await using (var rejudge = new NoCtfDbContext(options))
                {
                    await BackendMessageHandlers.Handle(
                        new DrainSubmissions(
                            fixture.CompetitionId,
                            fixture.CompetitionChallengeId,
                            fixture.Now.AddMinutes(2),
                            Rejudge: true,
                            submissionId),
                        rejudge,
                        outbox,
                        cancellationToken);
                }
                var message = outbox.Published.Skip(beforeRejudge)
                    .OfType<EvaluateSubmission>()
                    .Single(item => item.SubmissionId == submissionId);
                await ProcessAsync(
                    options,
                    outbox,
                    submissionId,
                    message.ProcessingVersion,
                    cancellationToken);

                await AssertCurrentAsync(
                    options,
                    submissionId,
                    ScoringResult.Correct,
                    failureCode: null,
                    cancellationToken);
                await using var verify = new NoCtfDbContext(options);
                var scoringEvents = await verify.ScoringEvents.IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(scoringEvent => scoringEvent.SubmissionId == submissionId)
                    .ToListAsync(cancellationToken);
                await Assert.That(scoringEvents).Count().IsEqualTo(2);
                await Assert.That(scoringEvents.Single(item => item.DeletedAt is not null).DeletedAt)
                    .IsNotNull();
                await Assert.That(await ScoreAsync(
                        verify,
                        fixture.CompetitionId,
                        fixture.TeamId,
                        fixture.Now.AddMinutes(2),
                        cancellationToken))
                    .IsEqualTo(40);
            }
        });
    }

    private static async Task AssertCurrentAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid submissionId,
        ScoringResult result,
        ScoringFailureCode? failureCode,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var current = await db.Submissions.AsNoTracking()
            .Where(submission => submission.Id == submissionId)
            .Join(
                db.ScoringEvents.AsNoTracking(),
                submission => submission.CurrentScoringEventId,
                scoringEvent => (Guid?)scoringEvent.Id,
                (_, scoringEvent) => scoringEvent)
            .SingleAsync(cancellationToken);
        await Assert.That(current.Result).IsEqualTo(result);
        await Assert.That(current.FailureCode).IsEqualTo(failureCode);
    }

    private static async Task ProcessAsync(
        DbContextOptions<NoCtfDbContext> options,
        ITransactionalMessageOutbox outbox,
        Guid submissionId,
        long processingVersion,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        using var fusion = new FusionCache(new FusionCacheOptions());
        var snapshots = SnapshotFactory(db, fusion);
        var processor = new SubmissionProcessor(
            db,
            Substitute.For<ISubmissionEvaluatorCatalog>(),
            new GameModeSubmissionAdmissionPolicy(),
            Substitute.For<IRuntimePlacementPolicy>(),
            outbox,
            snapshots);
        await processor.ProcessAsync(submissionId, processingVersion, cancellationToken);
    }

    private static async Task<long> ScoreAsync(
        NoCtfDbContext db,
        Guid competitionId,
        Guid teamId,
        DateTimeOffset projectedAt,
        CancellationToken cancellationToken)
    {
        using var fusion = new FusionCache(new FusionCacheOptions());
        var snapshot = await SnapshotFactory(db, fusion).CreateAsync(
            competitionId,
            projectedAt,
            historical: false,
            cancellationToken);
        return snapshot!.Entries.Single(item => item.TeamId == teamId).Score;
    }

    private static FusionLeaderboardCache SnapshotFactory(
        NoCtfDbContext db,
        IFusionCache fusion) =>
        new(
            db,
            new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
            new ConfigurationBuilder().Build(),
            new NullPublisher(),
            fusion);

    private static async Task<ScopeFixture> SeedScopeAsync(
        DbContextOptions<NoCtfDbContext> options,
        GameMode mode,
        IReadOnlyList<long> hintCosts,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.Parse("2026-08-08T00:00:00Z")
            .AddMinutes((int)mode * 10);
        var ownerId = Guid.CreateVersion7(now);
        var memberId = Guid.CreateVersion7(now.AddMilliseconds(1));
        var competitionId = Guid.CreateVersion7(now.AddMilliseconds(2));
        var challengeId = Guid.CreateVersion7(now.AddMilliseconds(3));
        var competitionChallengeId = Guid.CreateVersion7(now.AddMilliseconds(4));
        var teamId = Guid.CreateVersion7(now.AddMilliseconds(5));
        var baselineSubmissionId = Guid.CreateVersion7(now.AddMilliseconds(6));
        var baselineEventId = Guid.CreateVersion7(now.AddMilliseconds(7));
        var hintIds = hintCosts.Select((_, index) =>
            Guid.CreateVersion7(now.AddMilliseconds(10 + index))).ToArray();
        var hintSubmissionIds = hintCosts.Select((_, index) =>
            Guid.CreateVersion7(now.AddMilliseconds(20 + index))).ToArray();
        db.Users.AddRange(
            NewUser(ownerId, $"owner-{mode}", now),
            NewUser(memberId, $"member-{mode}", now));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = $"{mode} hint scoring",
            OwnerId = ownerId,
            Mode = mode,
            Status = CompetitionStatus.Running,
            ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(mode),
            ConfigurationUpdatedAt = now,
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            RunningSince = now.AddHours(-1),
            FlagDerivationSecret = new byte[32],
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = mode,
            Title = $"{mode} challenge",
            Direction = "Misc",
            DefinitionJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            BaseScore = 100,
            Order = 1,
            IsPublished = true,
            RulesJson = new GameModeChallengeConfigurationCatalog().GetDefaultJson(mode),
            UpdatedAt = now,
            Hints = hintIds.Select((hintId, index) => new CompetitionChallengeHint
            {
                Id = hintId,
                Content = $"Hint {index + 1}",
                Cost = hintCosts[index],
                PublishedAt = now.AddMinutes(-1)
            }).ToList()
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = $"Team-{mode}",
            NormalizedName = $"TEAM-{mode}".ToUpperInvariant(),
            CaptainId = memberId,
            MemberIds = [memberId],
            InvitationToken = new string((char)('a' + (int)mode), 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.Submissions.Add(new Submission
        {
            Id = baselineSubmissionId,
            CompetitionId = competitionId,
            TeamId = teamId,
            CompetitionChallengeId = competitionChallengeId,
            SubmittedByUserId = ownerId,
            Kind = SubmissionKind.ManualAdjust,
            SubmittedFlag = "100",
            ReceivedAt = now.AddSeconds(-2),
            EvaluationState = SubmissionEvaluationState.Completed,
            EvaluationUpdatedAt = now.AddSeconds(-2),
            ProcessingVersion = 1
        });
        db.Submissions.AddRange(hintSubmissionIds.Select((submissionId, index) => new Submission
        {
            Id = submissionId,
            CompetitionId = competitionId,
            TeamId = teamId,
            CompetitionChallengeId = competitionChallengeId,
            SubmittedByUserId = memberId,
            Kind = SubmissionKind.HintUnlock,
            SubmittedFlag = hintIds[index].ToString("D"),
            ReceivedAt = now.AddMilliseconds(index),
            EvaluationState = SubmissionEvaluationState.Queued,
            EvaluationUpdatedAt = now.AddMilliseconds(index)
        }));
        await db.SaveChangesAsync(cancellationToken);
        db.ScoringEvents.Add(new ScoringEvent
        {
            Id = baselineEventId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            SubmissionId = baselineSubmissionId,
            TeamId = teamId,
            Kind = ScoringEventKind.ManualAdjust,
            Result = ScoringResult.Correct,
            ProcessingVersion = 1,
            OccurredAt = now.AddSeconds(-2),
            CreatedAt = now.AddSeconds(-2)
        });
        await db.SaveChangesAsync(cancellationToken);
        var baseline = await db.Submissions.SingleAsync(
            item => item.Id == baselineSubmissionId,
            cancellationToken);
        baseline.CurrentScoringEventId = baselineEventId;
        await db.SaveChangesAsync(cancellationToken);
        return new(
            now,
            competitionId,
            competitionChallengeId,
            teamId,
            hintSubmissionIds);
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

    private static DbContextOptions<NoCtfDbContext> Options(PostgreSqlContainer postgres) =>
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        string database,
        CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase(database)
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(cancellationToken);
        return postgres;
    }

    private sealed record ScopeFixture(
        DateTimeOffset Now,
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        Guid TeamId,
        IReadOnlyList<Guid> HintSubmissionIds);

    private sealed class NullPublisher : ILeaderboardRefreshPublisher
    {
        public Task PublishAsync(
            Guid competitionId,
            DateTimeOffset generatedAt,
            CancellationToken cancellationToken) => Task.CompletedTask;
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
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerPoolAsync<T>(
            T message,
            DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;

        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerNodeAsync<T>(
            T message,
            DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
