using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NoCTF.Application.CompetitionModes;
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
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContext(competitionId));
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
    public async Task ProcessSubmissionAsync_NotificationFailure_DoesNotRollbackAcceptedSolve()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        SeedChallenge(db, competitionId, challengeId, "flag{durable}");
        await db.SaveChangesAsync();
        var mode = new CtfGameMode(
            db,
            new ThrowingSubmissionEventHandler(),
            new NoopScoreSignalEmitter(),
            new NoopCtfScoreRebuilder(),
            new ChallengeSubmissionHandlerRegistry([]));

        var result = await mode.ProcessSubmissionAsync(
            CreateContext(competitionId, teamId, challengeId, "flag{durable}"));

        Assert.Equal(SubmissionResult.Accepted, result);
        Assert.True((await db.Submissions.IgnoreQueryFilters().SingleAsync()).IsCorrect);
    }

    [Fact]
    public async Task ProcessSubmissionAsync_DynamicFlagWithoutActiveInstance_RequiresInstance()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
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

    private static CtfGameMode CreateMode(ApplicationDbContext db)
        => new(
            db,
            new NoopSubmissionEventHandler(),
            new NoopScoreSignalEmitter(),
            new NoopCtfScoreRebuilder(),
            new ChallengeSubmissionHandlerRegistry([]));

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
}

file class FixedTenantContext(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
