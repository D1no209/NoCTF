using Microsoft.EntityFrameworkCore;
using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awdp.Scoring;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Persistence.UseCaseAdapters;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Submissions;

[Category("Integration")]
public sealed class AwdpVerificationCallbackStoreTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Callback_updates_fix_and_fact_atomically_and_replays_idempotently(
        CancellationToken cancellationToken)
    {
        await using var postgres = CreatePostgres("noctf_awdp_callback");
        await postgres.StartAsync(cancellationToken);
        var options = Options(postgres.GetConnectionString());
        var scope = await SeedAsync(options, includeFixRecord: true, cancellationToken);
        var occurredAt = scope.Now.AddMinutes(1);

        var first = await RecordAsync(options, scope, 0, occurredAt, cancellationToken);
        var replay = await RecordAsync(options, scope, 0, occurredAt, cancellationToken);
        var conflict = await RecordAsync(options, scope, 1, occurredAt, cancellationToken);

        await Assert.That(first.Created).IsTrue();
        await Assert.That(replay.Created).IsFalse();
        await Assert.That(replay.ScoringEventId).IsEqualTo(first.ScoringEventId);
        await Assert.That(conflict.Failure).IsEqualTo(SystemScoringEventRecordFailure.SourceConflict);
        await using var verify = new NoCtfDbContext(options);
        var fact = await verify.ScoringEvents.AsNoTracking().SingleAsync(cancellationToken);
        var fix = await verify.FixSubmissionRecords.AsNoTracking().SingleAsync(cancellationToken);
        await Assert.That(fact.Result).IsEqualTo(ScoringResult.Correct);
        await Assert.That(fact.SubmissionId).IsEqualTo(scope.SubmissionId);
        await Assert.That(fix.VerificationStatus).IsEqualTo(FixVerificationStatus.Valid);
        await Assert.That(fix.VerifiedAt).IsEqualTo(occurredAt);
    }

    [Test]
    [Timeout(300_000)]
    public async Task Callback_without_verifying_fix_rolls_back_scoring_fact(
        CancellationToken cancellationToken)
    {
        await using var postgres = CreatePostgres("noctf_awdp_callback_rollback");
        await postgres.StartAsync(cancellationToken);
        var options = Options(postgres.GetConnectionString());
        var scope = await SeedAsync(options, includeFixRecord: false, cancellationToken);

        var result = await RecordAsync(options, scope, 0, scope.Now.AddMinutes(1), cancellationToken);

        await Assert.That(result.Failure).IsEqualTo(SystemScoringEventRecordFailure.SourceConflict);
        await using var verify = new NoCtfDbContext(options);
        await Assert.That(await verify.ScoringEvents.CountAsync(cancellationToken)).IsEqualTo(0);
    }

    private static PostgreSqlContainer CreatePostgres(string database) =>
        new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase(database)
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

    private static DbContextOptions<NoCtfDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(connectionString).Options;

    private static async Task<RecordSystemScoringEventResult> RecordAsync(
        DbContextOptions<NoCtfDbContext> options,
        CallbackScope scope,
        int exitCode,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var store = new EfSystemScoringEventStore(db);
        var scheduler = new NoopScheduler();
        var useCase = new RecordAwdpCheckResult(
            new AwdpCheckExitCodeMapper(),
            store,
            new RecordSystemScoringEvent(store, scheduler, scheduler));
        return await useCase.ExecuteAsync(new(
            scope.CompetitionId,
            scope.TeamId,
            scope.CompetitionChallengeId,
            scope.SubmissionId,
            AwdpVerificationPhase.Checker,
            exitCode,
            false,
            occurredAt,
            scope.SourceKey), cancellationToken);
    }

    private static async Task<CallbackScope> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        bool includeFixRecord,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.CreateVersion7(now);
        var competitionId = Guid.CreateVersion7(now.AddTicks(1));
        var teamId = Guid.CreateVersion7(now.AddTicks(2));
        var templateId = Guid.CreateVersion7(now.AddTicks(3));
        var competitionChallengeId = Guid.CreateVersion7(now.AddTicks(4));
        var submissionId = Guid.CreateVersion7(now.AddTicks(5));
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        db.Users.Add(new User
        {
            Id = userId,
            UserName = "awdp-captain",
            NormalizedUserName = "AWDP-CAPTAIN",
            Email = "awdp-captain@example.test",
            NormalizedEmail = "AWDP-CAPTAIN@EXAMPLE.TEST",
            PasswordHash = "not-used",
            Role = UserRole.User,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "AWDP callback",
            OwnerId = userId,
            Mode = GameMode.Awdp,
            ConfigurationJson = "{}",
            ConfigurationRevision = 0,
            ConfigurationUpdatedAt = now,
            Status = CompetitionStatus.Running,
            StartTime = now.AddHours(-1),
            EndTime = now.AddHours(1),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "AWDP team",
            InvitationToken = "0123456789ABCDEF0123456789ABCDEF",
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = templateId,
            Title = "AWDP template",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = templateId,
            BaseScore = 100,
            Order = 0,
            IsPublished = true,
            ConfigurationJson = "{}",
            Revision = 0,
            UpdatedAt = now
        });
        db.Submissions.Add(new Submission
        {
            Id = submissionId,
            CompetitionId = competitionId,
            TeamId = teamId,
            CompetitionChallengeId = competitionChallengeId,
            UserId = userId,
            Kind = SubmissionKind.Fix,
            ReceivedAt = now,
            ProcessingVersion = 0,
            CreatedAt = now,
            UpdatedAt = now
        });
        if (includeFixRecord)
        {
            db.FixSubmissionRecords.Add(new FixSubmissionRecord
            {
                UploadId = Guid.CreateVersion7(now.AddTicks(6)),
                SubmissionId = submissionId,
                CompetitionId = competitionId,
                TeamId = teamId,
                CompetitionChallengeId = competitionChallengeId,
                ObjectKey = "fixes/archive.tar",
                ExpiresAt = now.AddHours(1),
                VerificationStatus = FixVerificationStatus.Verifying,
                CreatedAt = now,
                UpdatedAt = now,
                RowVersion = 1
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        return new(competitionId, teamId, competitionChallengeId, submissionId,
            $"awdp:{competitionId:N}:{competitionChallengeId:N}:{teamId:N}:fix:{submissionId:N}", now);
    }

    private sealed record CallbackScope(
        Guid CompetitionId,
        Guid TeamId,
        Guid CompetitionChallengeId,
        Guid SubmissionId,
        string SourceKey,
        DateTimeOffset Now);

    private sealed class NoopScheduler : IBackgroundWorkScheduler, IBackgroundWorkAdmissionGate
    {
        public bool IsAccepting => true;
        public IBackgroundWorkAdmissionLease TryEnter() => new Lease();
        public ValueTask EnqueueSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EnqueueSystemEventAsync(Guid scoringEventId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EnqueueLeaderboardRefreshAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EnqueueCompetitionRebuildAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        private sealed class Lease : IBackgroundWorkAdmissionLease
        {
            public CancellationToken DrainCancellation => CancellationToken.None;
            public void Dispose() { }
        }
    }
}
