using Microsoft.EntityFrameworkCore;
using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Persistence.UseCaseAdapters;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Submissions;

[Category("Integration")]
public sealed class SubmissionMaxAttemptConcurrencyTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_flags_do_not_exceed_max_attempts(CancellationToken cancellationToken)
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("noctf_max_attempts")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(cancellationToken);

        var options = new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        var scope = await SeedAsync(options, cancellationToken);
        var gate = new SchedulerGate();
        using var policy = new SingleAttemptPolicy();

        var results = await Task.WhenAll(
            SubmitAsync(options, scope, "attempt-1", gate, policy, cancellationToken),
            SubmitAsync(options, scope, "attempt-2", gate, policy, cancellationToken));

        await Assert.That(results.Count(result => result.Succeeded)).IsEqualTo(1);
        await Assert.That(results.Count(result => result.ErrorCode == "attempts_exhausted")).IsEqualTo(1);
        await using var verify = new NoCtfDbContext(options);
        await Assert.That(await verify.Submissions.CountAsync(cancellationToken)).IsEqualTo(1);
        await Assert.That(gate.Enqueued).IsEqualTo(1);
    }

    private static async Task<NoCTF.Application.Common.OperationResult<SubmissionAccepted>> SubmitAsync(
        DbContextOptions<NoCtfDbContext> options,
        SubmissionScope scope,
        string idempotencyKey,
        SchedulerGate gate,
        SingleAttemptPolicy policy,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var useCase = new SubmitFlag(new EfSubmissionIntakeStore(db, gate, gate), policy);
        return await useCase.ExecuteAsync(new(
            scope.CompetitionId,
            scope.TeamId,
            scope.ChallengeId,
            scope.UserId,
            "FLAG{concurrent}",
            idempotencyKey,
            "127.0.0.1",
            scope.Now), cancellationToken);
    }

    private static async Task<SubmissionScope> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.CreateVersion7(now);
        var competitionId = Guid.CreateVersion7(now.AddTicks(1));
        var teamId = Guid.CreateVersion7(now.AddTicks(2));
        var challengeId = Guid.CreateVersion7(now.AddTicks(3));
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);
        db.Users.Add(new User
        {
            Id = userId,
            UserName = "competitor",
            NormalizedUserName = "COMPETITOR",
            Email = "competitor@example.test",
            NormalizedEmail = "COMPETITOR@EXAMPLE.TEST",
            PasswordHash = "not-used",
            Role = UserRole.User,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            Title = "Max attempt race",
            OwnerId = userId,
            Mode = GameMode.Ctf,
            ConfigurationJson = """{"schemaVersion":1}""",
            ConfigurationRevision = 1,
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
            Name = "Race team",
            InvitationToken = "0123456789ABCDEF0123456789ABCDEF",
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.Teams.Local.Single(item => item.Id == teamId).Members.Add(new TeamMember
        {
            Id = Guid.CreateVersion7(now.AddTicks(4)),
            UserId = userId,
            MemberOrder = 0,
            JoinedAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = Guid.CreateVersion7(now.AddTicks(5)),
            Title = "Race challenge",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = challengeId,
            CompetitionId = competitionId,
            ChallengeId = db.Challenges.Local.Single().Id,
            BaseScore = 100,
            Order = 0,
            IsPublished = true,
            ConfigurationJson = """{"schemaVersion":1}""",
            Revision = 1,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new(competitionId, teamId, challengeId, userId, now);
    }

    private sealed record SubmissionScope(
        Guid CompetitionId,
        Guid TeamId,
        Guid ChallengeId,
        Guid UserId,
        DateTimeOffset Now);

    private sealed class SingleAttemptPolicy : ISubmissionAdmissionModePolicy, IDisposable
    {
        private readonly Barrier _snapshotBarrier = new(2);

        public SubmissionAdmissionRules GetRules(
            GameMode mode,
            string competitionConfigurationJson,
            string challengeConfigurationJson)
        {
            if (!_snapshotBarrier.SignalAndWait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("Concurrent submissions did not both reach the admission barrier.");
            return new(true, false, 1, null);
        }

        public void Dispose() => _snapshotBarrier.Dispose();
    }

    private sealed class SchedulerGate : IBackgroundWorkScheduler, IBackgroundWorkAdmissionGate
    {
        private int _enqueued;
        public bool IsAccepting => true;
        public int Enqueued => _enqueued;
        public IBackgroundWorkAdmissionLease TryEnter() => new Lease();
        public ValueTask EnqueueSubmissionAsync(Guid submissionId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _enqueued);
            return ValueTask.CompletedTask;
        }

        public ValueTask EnqueueSystemEventAsync(Guid scoringEventId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
        public ValueTask EnqueueLeaderboardRefreshAsync(Guid competitionId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
        public ValueTask EnqueueCompetitionRebuildAsync(Guid competitionId, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        private sealed class Lease : IBackgroundWorkAdmissionLease
        {
            public CancellationToken DrainCancellation => CancellationToken.None;
            public void Dispose() { }
        }
    }
}
