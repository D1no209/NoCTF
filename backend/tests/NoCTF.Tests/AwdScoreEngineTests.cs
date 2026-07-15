using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Plugins.AWD;

namespace NoCTF.Tests;

/// <summary>
/// TDD tests for AwdScoreEngine and AwdGameMode.ProcessSubmissionAsync.
/// </summary>
public class AwdScoreEngineTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContextScoring(competitionId));
    }

    private static AwdScoreEngine CreateScoreEngine(ApplicationDbContext db)
        => new(db, new NullLeaderboardService(), new NullRedisLeaderboardCache(), new NullHubNotifier(), CreateScoreSignalEmitter(db), NullLogger<AwdScoreEngine>.Instance);

    private static AwdGameMode CreateGameMode(
        ApplicationDbContext db,
        ICompetitionExecutionLease? executionLease = null)
        => new(db, new NullHubNotifier(), CreateScoreSignalEmitter(db), executionLease: executionLease);

    private static IScoreSignalEmitter CreateScoreSignalEmitter(ApplicationDbContext db)
    {
        var writer = new ScoreEventWriter(db);
        IScoringStrategy[] strategies = [new RoundAccumulationScoringStrategy(db, writer)];
        return new ScoreSignalEmitter(
            db,
            strategies,
            new CompetitionScoringProfileResolver(db),
            NullLogger<ScoreSignalEmitter>.Instance);
    }

    private static void SeedCompetition(ApplicationDbContext db, Guid competitionId,
        int attackPoints = 50, int serviceOnlinePoints = 100, int serviceDownPenalty = 50,
        int beenAttackedPenalty = 50, int flagValidityRounds = 2)
    {
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "Test AWD",
            OwnerId = Guid.NewGuid(),
            GameModeType = GameModeType.Awd,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(2),
            Status = CompetitionStatus.Running,
            AttackPoints = attackPoints,
            ServiceOnlinePoints = serviceOnlinePoints,
            ServiceDownPenalty = serviceDownPenalty,
            BeenAttackedPenalty = beenAttackedPenalty,
            FlagValidityRounds = flagValidityRounds
        });
    }

    private static Guid SeedTeam(ApplicationDbContext db, Guid competitionId, string name = "Team")
    {
        var id = Guid.NewGuid();
        db.Teams.Add(new Team
        {
            Id = id,
            CompetitionId = competitionId,
            Name = name,
            CaptainId = Guid.NewGuid(),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            CreatedAt = DateTime.UtcNow
        });
        return id;
    }

    private static Guid SeedChallenge(
        ApplicationDbContext db,
        Guid competitionId,
        PointsConfig? pointsConfig = null)
    {
        var id = Guid.NewGuid();
        db.Challenges.Add(new Challenge
        {
            Id = id,
            CompetitionId = competitionId,
            Title = "Challenge",
            TypeId = "ctf",
            PointsConfig = pointsConfig ?? new PointsConfig(),
            CreatedAt = DateTime.UtcNow
        });
        return id;
    }

    private static void SeedCheckResult(ApplicationDbContext db, Guid competitionId,
        Guid teamId, Guid challengeId, int round, AwdCheckStatus status)
    {
        db.AwdCheckResults.Add(new AwdCheckResult
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            RoundNumber = round,
            Status = status,
            CheckedAt = DateTime.UtcNow
        });
    }

    private static void SeedAttackRecord(ApplicationDbContext db, Guid competitionId,
        Guid attackerTeamId, Guid victimTeamId, Guid challengeId, int round, string flag = "flag{x}")
    {
        db.AwdAttackRecords.Add(new AwdAttackRecord
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            AttackerTeamId = attackerTeamId,
            VictimTeamId = victimTeamId,
            ChallengeId = challengeId,
            RoundNumber = round,
            FlagContent = flag,
            Timestamp = DateTime.UtcNow
        });
    }

    private static void SeedFlag(ApplicationDbContext db, Guid competitionId,
        Guid teamId, Guid challengeId, int round, string flagContent)
    {
        db.AwdFlags.Add(new AwdFlag
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            RoundNumber = round,
            FlagContent = flagContent,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static void SeedRound(ApplicationDbContext db, Guid competitionId, int roundNumber)
    {
        db.AwdRounds.Add(new AwdRound
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            RoundNumber = roundNumber,
            StartTime = DateTime.UtcNow,
            Status = AwdRoundStatus.Running
        });
    }

    // ── CalculateRoundScoreAsync tests ────────────────────────────────────────

    [Fact]
    public async Task CalculateRoundScoreAsync_HealthyService_GainsPoints()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId, serviceOnlinePoints: 100);
        var teamId = SeedTeam(db, competitionId);
        var challengeId = SeedChallenge(db, competitionId);
        SeedCheckResult(db, competitionId, teamId, challengeId, round: 1, AwdCheckStatus.Healthy);
        await db.SaveChangesAsync();

        var engine = CreateScoreEngine(db);
        await engine.CalculateRoundScoreAsync(competitionId, roundNumber: 1);

        var events = await db.ScoreEvents.IgnoreQueryFilters().ToListAsync();
        Assert.Single(events);
        Assert.Equal(100, events[0].PointsDelta);
        Assert.Equal(teamId, events[0].TeamId);
        Assert.Equal("awd.service-online", events[0].EventType);
        Assert.Equal(1, events[0].RoundNumber);
    }

    [Fact]
    public async Task CalculateRoundScoreAsync_DownService_LosesPoints()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId, serviceDownPenalty: 50);
        var teamId = SeedTeam(db, competitionId);
        var challengeId = SeedChallenge(db, competitionId);
        SeedCheckResult(db, competitionId, teamId, challengeId, round: 1, AwdCheckStatus.Down);
        await db.SaveChangesAsync();

        var engine = CreateScoreEngine(db);
        await engine.CalculateRoundScoreAsync(competitionId, roundNumber: 1);

        var events = await db.ScoreEvents.IgnoreQueryFilters().ToListAsync();
        Assert.Single(events);
        Assert.Equal(-50, events[0].PointsDelta);
    }

    [Fact]
    public async Task CalculateRoundScoreAsync_Attacked_LosesPenalty()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId, serviceOnlinePoints: 100, beenAttackedPenalty: 50);
        var victimTeamId = SeedTeam(db, competitionId, "Victim");
        var attackerTeamId = SeedTeam(db, competitionId, "Attacker");
        var challengeId = SeedChallenge(db, competitionId);
        SeedCheckResult(db, competitionId, victimTeamId, challengeId, round: 1, AwdCheckStatus.Healthy);
        SeedCheckResult(db, competitionId, attackerTeamId, challengeId, round: 1, AwdCheckStatus.Healthy);
        SeedAttackRecord(db, competitionId, attackerTeamId, victimTeamId, challengeId, round: 1);
        await db.SaveChangesAsync();

        var engine = CreateScoreEngine(db);
        await engine.CalculateRoundScoreAsync(competitionId, roundNumber: 1);

        var events = await db.ScoreEvents.IgnoreQueryFilters().ToListAsync();
        // Victim: +100 (healthy) - 50 (attacked) = +50 across two scoring facts
        Assert.Equal(50, events.Where(e => e.TeamId == victimTeamId).Sum(e => e.PointsDelta));
        // Attacker: +100 (healthy) + 50 (attack record rebuilt)
        Assert.Equal(150, events.Where(e => e.TeamId == attackerTeamId).Sum(e => e.PointsDelta));
    }

    [Fact]
    public async Task CalculateRoundScoreAsync_NoCheckResult_NoScoreEvent()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId);
        SeedTeam(db, competitionId);
        SeedChallenge(db, competitionId);
        // No check results seeded — delta = 0, no event written
        await db.SaveChangesAsync();

        var engine = CreateScoreEngine(db);
        await engine.CalculateRoundScoreAsync(competitionId, roundNumber: 1);

        var events = await db.ScoreEvents.IgnoreQueryFilters().ToListAsync();
        Assert.Empty(events);
    }

    [Fact]
    public async Task CalculateRoundScoreAsync_HealthyService_UsesSolveDecay()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId, serviceOnlinePoints: 100);
        var firstTeamId = SeedTeam(db, competitionId, "First");
        var secondTeamId = SeedTeam(db, competitionId, "Second");
        var challengeId = SeedChallenge(
            db,
            competitionId,
            new PointsConfig(InitialPoints: 500, MinimumPoints: 25, DecayFactor: 1, DecayFunction: "linear"));
        SeedCheckResult(db, competitionId, firstTeamId, challengeId, round: 1, AwdCheckStatus.Healthy);
        SeedCheckResult(db, competitionId, secondTeamId, challengeId, round: 1, AwdCheckStatus.Healthy);
        await db.SaveChangesAsync();

        var engine = CreateScoreEngine(db);
        await engine.CalculateRoundScoreAsync(competitionId, roundNumber: 1);

        var events = await db.ScoreEvents.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, scoreEvent => Assert.Equal(25, scoreEvent.PointsDelta));
    }

    [Fact]
    public async Task CalculateRoundScoreAsync_MultipleCells_UsesOneBatchWithLatestChecks()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        var firstTeamId = SeedTeam(db, competitionId, "First");
        var secondTeamId = SeedTeam(db, competitionId, "Second");
        var challengeId = SeedChallenge(db, competitionId);
        var now = DateTime.UtcNow;
        db.AwdCheckResults.AddRange(
            new AwdCheckResult
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                TeamId = firstTeamId,
                ChallengeId = challengeId,
                RoundNumber = 1,
                Status = AwdCheckStatus.Healthy,
                CheckedAt = now.AddSeconds(-10)
            },
            new AwdCheckResult
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                TeamId = firstTeamId,
                ChallengeId = challengeId,
                RoundNumber = 1,
                Status = AwdCheckStatus.Down,
                CheckedAt = now
            },
            new AwdCheckResult
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                TeamId = secondTeamId,
                ChallengeId = challengeId,
                RoundNumber = 1,
                Status = AwdCheckStatus.Healthy,
                CheckedAt = now
            });
        SeedAttackRecord(db, competitionId, firstTeamId, secondTeamId, challengeId, round: 1);
        await db.SaveChangesAsync();

        var emitter = new RecordingBatchSignalEmitter();
        var engine = new AwdScoreEngine(
            db,
            new NullLeaderboardService(),
            new NullRedisLeaderboardCache(),
            new NullHubNotifier(),
            emitter,
            NullLogger<AwdScoreEngine>.Instance);

        await engine.CalculateRoundScoreAsync(competitionId, roundNumber: 1);

        Assert.Equal(1, emitter.BatchCalls);
        Assert.Equal(0, emitter.SingleCalls);
        Assert.Equal(4, emitter.Signals.Count);
        Assert.Contains(emitter.Signals, signal =>
            signal.TeamId == firstTeamId && signal.SignalType == ScoreSignalTypes.AttackAccepted);
        Assert.Contains(emitter.Signals, signal =>
            signal.TeamId == firstTeamId && signal.SignalType == ScoreSignalTypes.ServiceCheckFailed);
        Assert.DoesNotContain(emitter.Signals, signal =>
            signal.TeamId == firstTeamId && signal.SignalType == ScoreSignalTypes.ServiceCheckPassed);
        Assert.Contains(emitter.Signals, signal =>
            signal.TeamId == secondTeamId && signal.SignalType == ScoreSignalTypes.ServiceAttacked);
    }

    // ── ProcessSubmissionAsync tests ──────────────────────────────────────────

    [Fact]
    public async Task ProcessSubmissionAsync_ValidAttack_GainsPoints()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId, attackPoints: 50);
        var attackerTeamId = SeedTeam(db, competitionId, "Attacker");
        var victimTeamId = SeedTeam(db, competitionId, "Victim");
        var challengeId = SeedChallenge(db, competitionId);
        SeedRound(db, competitionId, roundNumber: 1);
        const string flagContent = "flag{victim-round1}";
        SeedFlag(db, competitionId, victimTeamId, challengeId, round: 1, flagContent);
        await db.SaveChangesAsync();

        var gameMode = CreateGameMode(db);
        var ctx = new SubmissionContext(competitionId, attackerTeamId, challengeId, Guid.NewGuid(), flagContent, "127.0.0.1");
        var result = await gameMode.ProcessSubmissionAsync(ctx);

        Assert.Equal(SubmissionResult.Accepted, result);

        var attackRecords = await db.AwdAttackRecords.IgnoreQueryFilters().ToListAsync();
        Assert.Single(attackRecords);
        Assert.Equal(attackerTeamId, attackRecords[0].AttackerTeamId);
        Assert.Equal(victimTeamId, attackRecords[0].VictimTeamId);

        var scoreEvents = await db.ScoreEvents.IgnoreQueryFilters().ToListAsync();
        var attackerEvent = scoreEvents.Single(e => e.TeamId == attackerTeamId);
        Assert.Equal(50, attackerEvent.PointsDelta);
    }

    [Fact]
    public async Task ProcessSubmissionAsync_DuplicateAttack_RepairsMissingScoreWithoutDoublePoints()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId, attackPoints: 50);
        var attackerTeamId = SeedTeam(db, competitionId, "Attacker");
        var victimTeamId = SeedTeam(db, competitionId, "Victim");
        var challengeId = SeedChallenge(db, competitionId);
        SeedRound(db, competitionId, roundNumber: 1);
        const string flagContent = "flag{victim-round1}";
        SeedFlag(db, competitionId, victimTeamId, challengeId, round: 1, flagContent);
        // Pre-seed existing attack record for same attacker/victim/challenge/round
        SeedAttackRecord(db, competitionId, attackerTeamId, victimTeamId, challengeId, round: 1, flagContent);
        await db.SaveChangesAsync();

        var gameMode = CreateGameMode(db);
        var ctx = new SubmissionContext(competitionId, attackerTeamId, challengeId, Guid.NewGuid(), flagContent, "127.0.0.1");
        var result = await gameMode.ProcessSubmissionAsync(ctx);
        var secondResult = await gameMode.ProcessSubmissionAsync(ctx);

        Assert.Equal(SubmissionResult.WrongFlag, result);
        Assert.Equal(SubmissionResult.WrongFlag, secondResult);

        var scoreEvents = await db.ScoreEvents.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, scoreEvents.Count);
        Assert.Equal(50, scoreEvents.Single(e => e.TeamId == attackerTeamId).PointsDelta);
        Assert.Equal(-50, scoreEvents.Single(e => e.TeamId == victimTeamId).PointsDelta);
    }

    [Fact]
    public async Task ProcessSubmissionAsync_SignalFailure_IsRepairableByIdempotentRetry()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId, attackPoints: 50);
        var attackerTeamId = SeedTeam(db, competitionId, "Attacker");
        var victimTeamId = SeedTeam(db, competitionId, "Victim");
        var challengeId = SeedChallenge(db, competitionId);
        SeedRound(db, competitionId, roundNumber: 1);
        const string flagContent = "flag{recoverable}";
        SeedFlag(db, competitionId, victimTeamId, challengeId, round: 1, flagContent);
        await db.SaveChangesAsync();

        var emitter = new FailOnceBatchSignalEmitter(CreateScoreSignalEmitter(db));
        var gameMode = new AwdGameMode(db, new NullHubNotifier(), emitter);
        var context = new SubmissionContext(
            competitionId, attackerTeamId, challengeId, Guid.NewGuid(), flagContent, "127.0.0.1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => gameMode.ProcessSubmissionAsync(context));
        Assert.Single(await db.AwdAttackRecords.IgnoreQueryFilters().ToListAsync());

        var retryResult = await gameMode.ProcessSubmissionAsync(context);

        Assert.Equal(SubmissionResult.WrongFlag, retryResult);
        Assert.Equal(2, await db.ScoreSignals.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, await db.ScoreEvents.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task ProcessSubmissionAsync_NotificationFailure_DoesNotLoseAttackOrScoring()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId, attackPoints: 50);
        var attackerTeamId = SeedTeam(db, competitionId, "Attacker");
        var victimTeamId = SeedTeam(db, competitionId, "Victim");
        var challengeId = SeedChallenge(db, competitionId);
        SeedRound(db, competitionId, roundNumber: 1);
        const string flagContent = "flag{notify-down}";
        SeedFlag(db, competitionId, victimTeamId, challengeId, round: 1, flagContent);
        await db.SaveChangesAsync();

        var gameMode = new AwdGameMode(db, new ThrowingAttackHubNotifier(), CreateScoreSignalEmitter(db));
        var result = await gameMode.ProcessSubmissionAsync(new SubmissionContext(
            competitionId, attackerTeamId, challengeId, Guid.NewGuid(), flagContent, "127.0.0.1"));

        Assert.Equal(SubmissionResult.Accepted, result);
        Assert.Single(await db.AwdAttackRecords.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(2, await db.ScoreEvents.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task ProcessSubmissionAsync_SelfAttack_Rejected()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId);
        var teamId = SeedTeam(db, competitionId, "Team");
        var challengeId = SeedChallenge(db, competitionId);
        SeedRound(db, competitionId, roundNumber: 1);
        const string flagContent = "flag{own-flag}";
        SeedFlag(db, competitionId, teamId, challengeId, round: 1, flagContent);
        await db.SaveChangesAsync();

        var gameMode = CreateGameMode(db);
        var ctx = new SubmissionContext(competitionId, teamId, challengeId, Guid.NewGuid(), flagContent, "127.0.0.1");
        var result = await gameMode.ProcessSubmissionAsync(ctx);

        Assert.Equal(SubmissionResult.WrongFlag, result);
    }

    [Fact]
    public async Task ProcessSubmissionAsync_ExpiredFlag_Rejected()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        // FlagValidityRounds = 1 means only current round is valid
        SeedCompetition(db, competitionId, flagValidityRounds: 1);
        var attackerTeamId = SeedTeam(db, competitionId, "Attacker");
        var victimTeamId = SeedTeam(db, competitionId, "Victim");
        var challengeId = SeedChallenge(db, competitionId);
        // Current round is 3, flag is from round 1 (too old)
        SeedRound(db, competitionId, roundNumber: 1);
        SeedRound(db, competitionId, roundNumber: 2);
        SeedRound(db, competitionId, roundNumber: 3);
        const string oldFlag = "flag{old-round1}";
        SeedFlag(db, competitionId, victimTeamId, challengeId, round: 1, oldFlag);
        await db.SaveChangesAsync();

        var gameMode = CreateGameMode(db);
        var ctx = new SubmissionContext(competitionId, attackerTeamId, challengeId, Guid.NewGuid(), oldFlag, "127.0.0.1");
        var result = await gameMode.ProcessSubmissionAsync(ctx);

        Assert.Equal(SubmissionResult.WrongFlag, result);
    }

    [Fact]
    public async Task ProcessSubmissionAsync_FutureRoundFlag_Rejected()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId);
        var attackerTeamId = SeedTeam(db, competitionId, "Attacker");
        var victimTeamId = SeedTeam(db, competitionId, "Victim");
        var challengeId = SeedChallenge(db, competitionId);
        SeedRound(db, competitionId, roundNumber: 1);
        const string futureFlag = "flag{future-round}";
        SeedFlag(db, competitionId, victimTeamId, challengeId, round: 2, futureFlag);
        await db.SaveChangesAsync();

        var gameMode = CreateGameMode(db);
        var ctx = new SubmissionContext(competitionId, attackerTeamId, challengeId, Guid.NewGuid(), futureFlag, "127.0.0.1");
        var result = await gameMode.ProcessSubmissionAsync(ctx);

        Assert.Equal(SubmissionResult.WrongFlag, result);
        Assert.Empty(await db.AwdAttackRecords.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.ScoreEvents.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task ProcessSubmissionAsync_UnknownFlag_ReturnsWrongFlag()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId);
        var attackerTeamId = SeedTeam(db, competitionId, "Attacker");
        var challengeId = SeedChallenge(db, competitionId);
        SeedRound(db, competitionId, roundNumber: 1);
        await db.SaveChangesAsync();

        var gameMode = CreateGameMode(db);
        var ctx = new SubmissionContext(competitionId, attackerTeamId, challengeId, Guid.NewGuid(), "flag{nonexistent}", "127.0.0.1");
        var result = await gameMode.ProcessSubmissionAsync(ctx);

        Assert.Equal(SubmissionResult.WrongFlag, result);
    }

    [Fact]
    public async Task ProcessSubmissionAsync_TombstonedChallenge_RejectsWithoutAttackOrScore()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        var attackerTeamId = SeedTeam(db, competitionId, "Attacker");
        var victimTeamId = SeedTeam(db, competitionId, "Victim");
        var challengeId = SeedChallenge(db, competitionId);
        SeedRound(db, competitionId, roundNumber: 1);
        const string flagContent = "flag{deleting-challenge}";
        SeedFlag(db, competitionId, victimTeamId, challengeId, round: 1, flagContent);
        await db.SaveChangesAsync();
        var challenge = await db.Challenges.IgnoreQueryFilters().SingleAsync(c => c.Id == challengeId);
        challenge.IsDeleting = true;
        await db.SaveChangesAsync();

        var result = await CreateGameMode(db).ProcessSubmissionAsync(new SubmissionContext(
            competitionId,
            attackerTeamId,
            challengeId,
            Guid.NewGuid(),
            flagContent,
            "127.0.0.1"));

        Assert.Equal(SubmissionResult.WrongFlag, result);
        Assert.Empty(await db.AwdAttackRecords.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.ScoreSignals.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.ScoreEvents.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task ProcessSubmissionAsync_ChallengeTombstonedAfterFlagPreRead_DoesNotRecreateAttackOrScore()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        var attackerTeamId = SeedTeam(db, competitionId, "Attacker");
        var victimTeamId = SeedTeam(db, competitionId, "Victim");
        var challengeId = SeedChallenge(db, competitionId);
        SeedRound(db, competitionId, roundNumber: 1);
        const string flagContent = "flag{teardown-race}";
        SeedFlag(db, competitionId, victimTeamId, challengeId, round: 1, flagContent);
        await db.SaveChangesAsync();
        var lease = new MutatingExecutionLease(async (leaseDb, ct) =>
        {
            var challenge = await leaseDb.Challenges.IgnoreQueryFilters().SingleAsync(c => c.Id == challengeId, ct);
            challenge.IsDeleting = true;
            await leaseDb.SaveChangesAsync(ct);
        });

        var result = await CreateGameMode(db, lease).ProcessSubmissionAsync(new SubmissionContext(
            competitionId,
            attackerTeamId,
            challengeId,
            Guid.NewGuid(),
            flagContent,
            "127.0.0.1"));

        Assert.Equal(SubmissionResult.WrongFlag, result);
        Assert.Empty(await db.AwdAttackRecords.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.ScoreSignals.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.ScoreEvents.IgnoreQueryFilters().ToListAsync());
    }

    // ── Null stubs ────────────────────────────────────────────────────────────

    private sealed class NullLeaderboardService : ILeaderboardService
    {
        public Task<IReadOnlyList<LeaderboardEntry>> CalculateLeaderboardAsync(
            Guid competitionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LeaderboardEntry>>(Array.Empty<LeaderboardEntry>());
    }

    private sealed class RecordingBatchSignalEmitter : IScoreSignalEmitter
    {
        public int SingleCalls { get; private set; }
        public int BatchCalls { get; private set; }
        public List<ScoreSignalCreate> Signals { get; } = [];

        public Task<ScoreSignal> EmitAsync(ScoreSignalCreate signal, CancellationToken ct = default)
        {
            SingleCalls++;
            return Task.FromResult(ToEntity(signal));
        }

        public Task<IReadOnlyList<ScoreSignal>> EmitBatchAsync(
            IReadOnlyCollection<ScoreSignalCreate> signals,
            CancellationToken ct = default)
        {
            BatchCalls++;
            Signals.AddRange(signals);
            return Task.FromResult<IReadOnlyList<ScoreSignal>>(signals.Select(ToEntity).ToList());
        }

        private static ScoreSignal ToEntity(ScoreSignalCreate signal)
            => new()
            {
                Id = Guid.NewGuid(),
                CompetitionId = signal.CompetitionId,
                TeamId = signal.TeamId,
                SignalType = signal.SignalType,
                IdempotencyKey = signal.IdempotencyKey,
                SubjectType = signal.SubjectType,
                SubjectId = signal.SubjectId,
                RoundNumber = signal.RoundNumber,
                OccurredAt = signal.OccurredAt ?? DateTime.UtcNow,
                PayloadJson = signal.PayloadJson
            };
    }

    private sealed class FailOnceBatchSignalEmitter(IScoreSignalEmitter inner) : IScoreSignalEmitter
    {
        private int _fail = 1;

        public Task<ScoreSignal> EmitAsync(ScoreSignalCreate signal, CancellationToken ct = default)
            => inner.EmitAsync(signal, ct);

        public Task<IReadOnlyList<ScoreSignal>> EmitBatchAsync(
            IReadOnlyCollection<ScoreSignalCreate> signals,
            CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _fail, 0) == 1)
                throw new InvalidOperationException("simulated scoring outage");
            return inner.EmitBatchAsync(signals, ct);
        }
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

    private class NullHubNotifier : IHubNotifierService
    {
        public Task NotifyScoreUpdateAsync(Guid competitionId, Guid teamId, string teamName, long newScore, int newRank, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyLeaderboardSnapshotAsync(Guid competitionId, IEnumerable<LeaderboardEntryPayload> entries, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyFlagSolvedAsync(Guid competitionId, Guid challengeId, string challengeName, Guid teamId, string teamName, bool isFirstBlood, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyChallengeUpdateAsync(Guid competitionId, Guid challengeId, string challengeName, string action, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyCompetitionStateChangeAsync(Guid competitionId, string state, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifySubmissionEventAsync(Guid competitionId, Guid submissionId, Guid teamId, string teamName, Guid challengeId, string challengeName, bool isCorrect, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyContainerEventAsync(Guid competitionId, Guid containerId, Guid challengeId, Guid teamId, string eventType, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifySystemAlertAsync(Guid competitionId, string level, string message, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyRoundStartedAsync(Guid competitionId, int roundNumber, CancellationToken ct = default) => Task.CompletedTask;
        public virtual Task NotifyAttackLogAsync(Guid competitionId, Guid attackerTeamId, string attackerTeamName, Guid victimTeamId, string victimTeamName, Guid challengeId, string challengeName, int roundNumber, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyKohUpdateAsync(Guid competitionId, Guid challengeId, Guid? controllerTeamId, DateTime timestamp, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ThrowingAttackHubNotifier : NullHubNotifier
    {
        public override Task NotifyAttackLogAsync(Guid competitionId, Guid attackerTeamId, string attackerTeamName, Guid victimTeamId, string victimTeamName, Guid challengeId, string challengeName, int roundNumber, CancellationToken ct = default)
            => throw new InvalidOperationException("simulated notification outage");
    }
}

/// <summary>Fixed tenant context for AwdScoreEngine tests.</summary>
file class FixedTenantContextScoring(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { /* no-op */ }
}
