using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application;
using NoCTF.Application.Leaderboard;
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
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContextScoring(competitionId));
    }

    private static AwdScoreEngine CreateScoreEngine(ApplicationDbContext db)
        => new(db, new NullLeaderboardService(), new NullHubNotifier(), NullLogger<AwdScoreEngine>.Instance);

    private static AwdGameMode CreateGameMode(ApplicationDbContext db)
        => new(db, new NullHubNotifier());

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
            CreatedAt = DateTime.UtcNow
        });
        return id;
    }

    private static Guid SeedChallenge(ApplicationDbContext db, Guid competitionId)
    {
        var id = Guid.NewGuid();
        db.Challenges.Add(new Challenge
        {
            Id = id,
            CompetitionId = competitionId,
            Title = "Challenge",
            TypeId = "ctf",
            PointsConfig = new PointsConfig(),
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
        Assert.Equal("awd_round_score", events[0].EventType);
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
        // Victim: +100 (healthy) - 50 (attacked) = +50
        var victimEvent = events.Single(e => e.TeamId == victimTeamId);
        Assert.Equal(50, victimEvent.PointsDelta);
        // Attacker: +100 (healthy), no penalty
        var attackerEvent = events.Single(e => e.TeamId == attackerTeamId);
        Assert.Equal(100, attackerEvent.PointsDelta);
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
    public async Task ProcessSubmissionAsync_DuplicateAttack_NoDoublePoints()
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

        Assert.Equal(SubmissionResult.WrongFlag, result);

        var scoreEvents = await db.ScoreEvents.IgnoreQueryFilters().ToListAsync();
        Assert.Empty(scoreEvents);
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

    // ── Null stubs ────────────────────────────────────────────────────────────

    private sealed class NullLeaderboardService : ILeaderboardService
    {
        public Task<IReadOnlyList<LeaderboardEntry>> CalculateLeaderboardAsync(
            Guid competitionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LeaderboardEntry>>(Array.Empty<LeaderboardEntry>());
    }

    private sealed class NullHubNotifier : IHubNotifierService
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
        public Task NotifyAttackLogAsync(Guid competitionId, Guid attackerTeamId, string attackerTeamName, Guid victimTeamId, string victimTeamName, Guid challengeId, string challengeName, int roundNumber, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyKohUpdateAsync(Guid competitionId, Guid challengeId, Guid? controllerTeamId, DateTime timestamp, CancellationToken ct = default) => Task.CompletedTask;
    }
}

/// <summary>Fixed tenant context for AwdScoreEngine tests.</summary>
file class FixedTenantContextScoring(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { /* no-op */ }
}
