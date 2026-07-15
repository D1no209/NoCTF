using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.Events;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Plugins.CTF;

namespace NoCTF.Tests;

public class CtfGameModeTests
{
    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider(ignoreTransactionWarnings: true)
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContext(competitionId));
    }

    private static ApplicationDbContext CreateDb(
        Guid competitionId,
        string databaseName,
        InMemoryDatabaseRoot databaseRoot,
        IInterceptor? interceptor = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider(databaseRoot, ignoreTransactionWarnings: true)
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        if (interceptor is not null)
            optionsBuilder.AddInterceptors(interceptor);

        return new ApplicationDbContext(optionsBuilder.Options, new FixedTenantContext(competitionId));
    }

    [Fact]
    public async Task ProcessSubmissionAsync_BeforeCompetitionStart_DoesNotRecordSolve()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2));
        SeedChallenge(db, competitionId, challengeId, "flag{soon}");
        await db.SaveChangesAsync();

        var result = await CreateMode(db).ProcessSubmissionAsync(
            CreateContext(competitionId, teamId, challengeId, "flag{soon}"));

        Assert.Equal(SubmissionResult.CompetitionNotStarted, result);
        Assert.Empty(db.Submissions);
    }

    [Fact]
    public async Task ProcessSubmissionAsync_AfterCompetitionEnd_DoesNotRecordSolve()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId, DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1));
        SeedChallenge(db, competitionId, challengeId, "flag{late}");
        await db.SaveChangesAsync();

        var result = await CreateMode(db).ProcessSubmissionAsync(
            CreateContext(competitionId, teamId, challengeId, "flag{late}"));

        Assert.Equal(SubmissionResult.CompetitionEnded, result);
        Assert.Empty(db.Submissions);
    }

    [Fact]
    public async Task ProcessSubmissionAsync_ChallengeFromAnotherCompetition_IsRejected()
    {
        var competitionId = Guid.NewGuid();
        var otherCompetitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        SeedCompetition(db, otherCompetitionId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        SeedChallenge(db, otherCompetitionId, challengeId, "flag{cross}");
        await db.SaveChangesAsync();

        var result = await CreateMode(db).ProcessSubmissionAsync(
            CreateContext(competitionId, teamId, challengeId, "flag{cross}"));

        Assert.Equal(SubmissionResult.WrongFlag, result);
        Assert.Empty(db.Submissions);
    }

    [Fact]
    public async Task ProcessSubmissionAsync_RunningCompetition_AcceptsCorrectFlag()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        SeedTeam(db, competitionId, teamId);
        SeedChallenge(db, competitionId, challengeId, "flag{ok}");
        await db.SaveChangesAsync();

        var result = await CreateMode(db).ProcessSubmissionAsync(
            CreateContext(competitionId, teamId, challengeId, "flag{ok}"));

        Assert.Equal(SubmissionResult.Accepted, result);
        var submission = Assert.Single(await db.Submissions.IgnoreQueryFilters().ToListAsync());
        Assert.True(submission.IsCorrect);
        Assert.Equal(challengeId, submission.ChallengeId);
    }

    [Fact]
    public async Task ProcessSubmissionAsync_DuplicateSolveConflict_ClearsFailedTrackedEntities()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        await using (var seedDb = CreateDb(competitionId, databaseName, databaseRoot))
        {
            SeedCompetition(seedDb, competitionId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
            SeedTeam(seedDb, competitionId, teamId);
            SeedChallenge(seedDb, competitionId, challengeId, "flag{duplicate}");
            await seedDb.SaveChangesAsync();
        }

        var conflict = new ConcurrentSolveConflictInterceptor(
            competitionId,
            teamId,
            challengeId,
            databaseName,
            databaseRoot);
        await using var db = CreateDb(competitionId, databaseName, databaseRoot, conflict);

        var result = await CreateMode(db).ProcessSubmissionAsync(
            CreateContext(competitionId, teamId, challengeId, "flag{duplicate}"));

        Assert.Equal(SubmissionResult.AlreadySolved, result);
        Assert.Empty(db.ChangeTracker.Entries());
        await using var verifyDb = CreateDb(competitionId, databaseName, databaseRoot);
        var persistedSolve = Assert.Single(await verifyDb.Submissions
            .IgnoreQueryFilters()
            .Where(submission => submission.IsCorrect)
            .ToListAsync());
        Assert.Equal(conflict.WinningSubmissionId, persistedSolve.Id);
    }

    [Fact]
    public async Task ProcessSubmissionAsync_NotificationFailure_DoesNotRollbackAcceptedSolve()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        SeedTeam(db, competitionId, teamId);
        SeedChallenge(db, competitionId, challengeId, "flag{durable}");
        await db.SaveChangesAsync();
        var backgroundTasks = new RecordingBackgroundTaskQueue();
        var mode = new CtfGameMode(
            db,
            new ThrowingSubmissionEventHandler(),
            new NoopScoreSignalEmitter(),
            new NoopCtfScoreRebuilder(),
            new ChallengeSubmissionHandlerRegistry([]),
            backgroundTasks);

        var result = await mode.ProcessSubmissionAsync(
            CreateContext(competitionId, teamId, challengeId, "flag{durable}"));

        Assert.Equal(SubmissionResult.Accepted, result);
        Assert.True((await db.Submissions.IgnoreQueryFilters().SingleAsync()).IsCorrect);
        var recoveryTask = Assert.Single(backgroundTasks.Enqueued);
        Assert.Equal(CtfScoreRebuildJobHandler.JobType, recoveryTask.Type);
        Assert.Equal(challengeId, Assert.IsType<CtfScoreRebuildPayload>(recoveryTask.Payload).ChallengeId);
    }

    [Fact]
    public async Task ProcessSubmissionAsync_ConcurrentCorrectSolves_EmitsExactlyOneFirstBloodEvent()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        var teamIds = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        await using (var seedDb = CreateDb(competitionId, databaseName, databaseRoot))
        {
            SeedCompetition(seedDb, competitionId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
            foreach (var teamId in teamIds)
                SeedTeam(seedDb, competitionId, teamId);
            SeedChallenge(seedDb, competitionId, challengeId, "flag{race}");
            await seedDb.SaveChangesAsync();
        }

        var events = new RecordingSubmissionEventHandler();
        var attempts = teamIds.Select(teamId => Task.Run(async () =>
        {
            await using var db = CreateDb(competitionId, databaseName, databaseRoot);
            var mode = new CtfGameMode(
                db,
                events,
                new NoopScoreSignalEmitter(),
                new NoopCtfScoreRebuilder(),
                new ChallengeSubmissionHandlerRegistry([]));
            return await mode.ProcessSubmissionAsync(
                CreateContext(competitionId, teamId, challengeId, "flag{race}"));
        }));

        var results = await Task.WhenAll(attempts);

        Assert.All(results, result => Assert.Equal(SubmissionResult.Accepted, result));
        Assert.Equal(teamIds.Length, events.Events.Count);
        Assert.Single(events.Events, solvedEvent => solvedEvent.IsFirstBlood);

        await using var verifyDb = CreateDb(competitionId, databaseName, databaseRoot);
        Assert.Equal(teamIds.Length, await verifyDb.Submissions.IgnoreQueryFilters().CountAsync(s => s.IsCorrect));
    }

    [Fact]
    public async Task CtfFirstBloodLock_SerializesConcurrentHoldersForSameChallenge()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        await using var firstDb = CreateDb(competitionId);
        await using var secondDb = CreateDb(competitionId);
        var firstLock = await CtfFirstBloodLock.AcquireAsync(
            firstDb,
            competitionId,
            challengeId,
            CancellationToken.None);

        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAttempt = Task.Run(async () =>
        {
            await using var secondLock = await CtfFirstBloodLock.AcquireAsync(
                secondDb,
                competitionId,
                challengeId,
                CancellationToken.None);
            secondEntered.SetResult();
        });

        await Task.Delay(50);
        Assert.False(secondEntered.Task.IsCompleted);
        await firstLock.DisposeAsync();
        await secondAttempt;
        Assert.True(secondEntered.Task.IsCompletedSuccessfully);
        Assert.False(CtfFirstBloodLock.HasLocalLock(competitionId, challengeId));
    }

    [Fact]
    public async Task CtfFirstBloodLock_CancelledWaiter_ReleasesKeyAfterHolderExits()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        await using var firstDb = CreateDb(competitionId);
        await using var secondDb = CreateDb(competitionId);
        await using var holder = await CtfFirstBloodLock.AcquireAsync(
            firstDb,
            competitionId,
            challengeId,
            CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var waiter = CtfFirstBloodLock.AcquireAsync(
            secondDb,
            competitionId,
            challengeId,
            cancellation.Token);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        Assert.True(CtfFirstBloodLock.HasLocalLock(competitionId, challengeId));

        await holder.DisposeAsync();
        Assert.False(CtfFirstBloodLock.HasLocalLock(competitionId, challengeId));
    }

    [Fact]
    public async Task ProcessSubmissionAsync_DynamicFlagWithoutActiveInstance_RequiresInstance()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        SeedTeam(db, competitionId, teamId);
        SeedChallenge(db, competitionId, challengeId, "[UUID]");
        db.CtfDynamicFlags.Add(new CtfDynamicFlag
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            FlagUuid = "d3adbeef-1111-4222-8333-aabbccddeeff",
            EnvironmentVariable = "FLAG",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await CreateMode(db).ProcessSubmissionAsync(
            CreateContext(competitionId, teamId, challengeId, "flag{d3adbeef-1111-4222-8333-aabbccddeeff}"));

        Assert.Equal(SubmissionResult.InstanceRequired, result);
        Assert.Empty(await db.Submissions.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task ProcessSubmissionAsync_DynamicFlagWithActiveInstance_AcceptsFlag()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        SeedTeam(db, competitionId, teamId);
        SeedChallenge(db, competitionId, challengeId, "[UUID]");
        db.CtfDynamicFlags.Add(new CtfDynamicFlag
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            FlagUuid = "d3adbeef-1111-4222-8333-aabbccddeeff",
            EnvironmentVariable = "FLAG",
            CreatedAt = DateTime.UtcNow
        });
        db.AwdGameBoxes.Add(new AwdGameBox
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            ContainerInstanceId = "container-123",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await CreateMode(db).ProcessSubmissionAsync(
            CreateContext(competitionId, teamId, challengeId, "flag{d3adbeef-1111-4222-8333-aabbccddeeff}"));

        Assert.Equal(SubmissionResult.Accepted, result);
        Assert.True((await db.Submissions.IgnoreQueryFilters().SingleAsync()).IsCorrect);
    }

    [Fact]
    public async Task ProcessSubmissionAsync_ChallengeTombstonedAfterPreRead_DoesNotRecreateSubmissionRows()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        SeedTeam(db, competitionId, teamId);
        SeedChallenge(db, competitionId, challengeId, "flag{teardown}");
        await db.SaveChangesAsync();
        var lease = new MutatingExecutionLease(async (leaseDb, ct) =>
        {
            var challenge = await leaseDb.Challenges.IgnoreQueryFilters().SingleAsync(c => c.Id == challengeId, ct);
            challenge.IsDeleting = true;
            await leaseDb.SaveChangesAsync(ct);
        });

        var result = await CreateMode(db, lease).ProcessSubmissionAsync(
            CreateContext(competitionId, teamId, challengeId, "flag{teardown}"));

        Assert.Equal(SubmissionResult.WrongFlag, result);
        Assert.Empty(await db.Submissions.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.CompetitionLogs.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task ProcessSubmissionAsync_CompetitionFinishedAfterPreRead_DoesNotRecreateSubmissionRows()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        SeedTeam(db, competitionId, teamId);
        SeedChallenge(db, competitionId, challengeId, "flag{teardown}");
        await db.SaveChangesAsync();
        var lease = new MutatingExecutionLease(async (leaseDb, ct) =>
        {
            var competition = await leaseDb.Competitions.IgnoreQueryFilters().SingleAsync(c => c.Id == competitionId, ct);
            competition.Status = CompetitionStatus.Finished;
            competition.EndTime = DateTime.UtcNow;
            await leaseDb.SaveChangesAsync(ct);
        });

        var result = await CreateMode(db, lease).ProcessSubmissionAsync(
            CreateContext(competitionId, teamId, challengeId, "flag{teardown}"));

        Assert.Equal(SubmissionResult.CompetitionEnded, result);
        Assert.Empty(await db.Submissions.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.CompetitionLogs.IgnoreQueryFilters().ToListAsync());
    }

    private static CtfGameMode CreateMode(
        ApplicationDbContext db,
        ICompetitionExecutionLease? executionLease = null)
        => new(
            db,
            new NoopSubmissionEventHandler(),
            new NoopScoreSignalEmitter(),
            new NoopCtfScoreRebuilder(),
            new ChallengeSubmissionHandlerRegistry([]),
            executionLease: executionLease);

    private static SubmissionContext CreateContext(
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        string flag)
        => new(
            CompetitionId: competitionId,
            TeamId: teamId,
            ChallengeId: challengeId,
            UserId: Guid.NewGuid(),
            FlagContent: flag,
            IpAddress: "127.0.0.1");

    private static void SeedCompetition(
        ApplicationDbContext db,
        Guid competitionId,
        DateTime start,
        DateTime end)
    {
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "CTF",
            GameModeType = GameModeType.Ctf,
            ModeKey = "ctf",
            OwnerId = Guid.NewGuid(),
            StartTime = start,
            EndTime = end,
            Status = CompetitionStatus.Published
        });
    }

    private static void SeedChallenge(
        ApplicationDbContext db,
        Guid competitionId,
        Guid challengeId,
        string flag)
    {
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            CompetitionId = competitionId,
            Title = "baby",
            TypeId = "PWN",
            FlagSecret = flag,
            PointsConfig = new PointsConfig(InitialPoints: 500, MinimumPoints: 100, DecayFactor: 450),
            CreatedAt = DateTime.UtcNow
        });
    }

    private static void SeedTeam(ApplicationDbContext db, Guid competitionId, Guid teamId)
    {
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = $"team-{teamId:N}",
            CaptainId = Guid.NewGuid(),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            CreatedAt = DateTime.UtcNow
        });
    }

    private sealed class NoopSubmissionEventHandler : ISubmissionEventHandler
    {
        public Task HandleAsync(SubmissionSolvedEvent solvedEvent, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class ThrowingSubmissionEventHandler : ISubmissionEventHandler
    {
        public Task HandleAsync(SubmissionSolvedEvent solvedEvent, CancellationToken ct = default)
            => throw new InvalidOperationException("notification unavailable");
    }

    private sealed class RecordingSubmissionEventHandler : ISubmissionEventHandler
    {
        public ConcurrentQueue<SubmissionSolvedEvent> Events { get; } = new();

        public Task HandleAsync(SubmissionSolvedEvent solvedEvent, CancellationToken ct = default)
        {
            Events.Enqueue(solvedEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class ConcurrentSolveConflictInterceptor(
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        string databaseName,
        InMemoryDatabaseRoot databaseRoot) : SaveChangesInterceptor
    {
        private int _triggered;

        public Guid WinningSubmissionId { get; } = Guid.NewGuid();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var db = (ApplicationDbContext?)eventData.Context;
            if (db is null ||
                !db.ChangeTracker.Entries<Submission>().Any(entry =>
                    entry.State == EntityState.Added && entry.Entity.IsCorrect) ||
                Interlocked.Exchange(ref _triggered, 1) != 0)
            {
                return result;
            }

            await using var winningDb = CreateDb(competitionId, databaseName, databaseRoot);
            winningDb.Submissions.Add(new Submission
            {
                Id = WinningSubmissionId,
                CompetitionId = competitionId,
                TeamId = teamId,
                ChallengeId = challengeId,
                UserId = Guid.NewGuid(),
                FlagContent = "sha256:winning",
                IsCorrect = true,
                SubmittedAt = DateTime.UtcNow,
                IpAddress = "127.0.0.1"
            });
            await winningDb.SaveChangesAsync(cancellationToken);

            throw new DbUpdateException("Simulated unique correct-solve conflict.");
        }
    }

    private sealed class NoopScoreSignalEmitter : IScoreSignalEmitter
    {
        public Task<ScoreSignal> EmitAsync(ScoreSignalCreate signal, CancellationToken ct = default)
            => Task.FromResult(new ScoreSignal
            {
                Id = Guid.NewGuid(),
                CompetitionId = signal.CompetitionId,
                TeamId = signal.TeamId,
                ActorUserId = signal.ActorUserId,
                SubjectType = signal.SubjectType,
                SubjectId = signal.SubjectId,
                SignalType = signal.SignalType,
                OccurredAt = signal.OccurredAt ?? DateTime.UtcNow,
                RoundNumber = signal.RoundNumber,
                PayloadJson = signal.PayloadJson,
                IdempotencyKey = signal.IdempotencyKey
            });
    }

    private sealed class NoopCtfScoreRebuilder : ICtfScoreRebuilder
    {
        public Task RebuildCompetitionAsync(Guid competitionId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RebuildChallengeAsync(Guid competitionId, Guid challengeId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class MutatingExecutionLease(
        Func<ApplicationDbContext, CancellationToken, Task> mutation) : ICompetitionExecutionLease
    {
        public async Task<IExecutionLease?> TryAcquireAsync(
            ApplicationDbContext db,
            string engineKey,
            Guid competitionId,
            CancellationToken ct = default)
        {
            Assert.Equal(CompetitionExecutionLeaseKeys.RuntimePreparation, engineKey);
            await mutation(db, ct);
            return new NoopExecutionLease();
        }
    }

    private sealed class NoopExecutionLease : IExecutionLease
    {
        public CancellationToken LostToken => CancellationToken.None;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingBackgroundTaskQueue : IBackgroundTaskQueue
    {
        public ConcurrentQueue<(string Type, object Payload)> Enqueued { get; } = new();

        public Task<Guid> EnqueueAsync<TPayload>(
            Guid competitionId,
            string type,
            TPayload payload,
            CancellationToken cancellationToken = default)
        {
            Enqueued.Enqueue((type, payload!));
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<BackgroundTaskItem?> TryAcquireNextAsync(
            TimeSpan lockDuration,
            CancellationToken cancellationToken = default)
            => Task.FromResult<BackgroundTaskItem?>(null);

        public Task<BackgroundTaskItem?> TryAcquireNextAsync(
            TimeSpan lockDuration,
            IReadOnlyCollection<string>? includedTypes,
            IReadOnlyCollection<string>? excludedTypes,
            CancellationToken cancellationToken = default)
            => Task.FromResult<BackgroundTaskItem?>(null);

        public Task<bool> RenewLeaseAsync(
            Guid taskId,
            string lockOwner,
            TimeSpan lockDuration,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task MarkSucceededAsync(
            Guid taskId,
            string lockOwner,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkFailedAsync(
            Guid taskId,
            string lockOwner,
            Exception exception,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> RecoverExpiredRunningTasksAsync(
            TimeSpan lockTimeout,
            CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }
}

file class FixedTenantContext(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
