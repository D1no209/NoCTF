using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Plugins.AWDP;

namespace NoCTF.Tests;

public class AwdpScoreEngineTests
{
    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContextAwdpScore(competitionId));
    }

    private static AwdpScoreEngine CreateEngine(ApplicationDbContext db)
        => new(
            db,
            new ScoreEventWriter(db),
            new NullLeaderboardServiceAwdp(),
            new NullRedisLeaderboardCache(),
            new NullHubNotifierAwdp(),
            new AwdpConfigResolver(db),
            NullLogger<AwdpScoreEngine>.Instance);

    [Fact]
    public async Task CalculateRoundScoreAsync_BreakAndFixSuccess_StartScoringNextRound()
    {
        var competitionId = Guid.NewGuid();
        var roundStart = DateTime.UtcNow;
        var successAt = roundStart.AddSeconds(30);
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        var teamId = SeedTeam(db, competitionId);
        var challengeId = SeedChallenge(db, competitionId, attackScore: 70, defenseScore: 130);
        SeedRound(db, competitionId, 1, roundStart);
        SeedRound(db, competitionId, 2, roundStart.AddMinutes(5));
        db.AwdpTeamChallengeStates.Add(new AwdpTeamChallengeState
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            BreakStatus = AwdpBreakStatus.BreakSuccess,
            FixStatus = AwdpFixStatus.FixSuccess,
            BreakSucceededAt = successAt,
            FixSucceededAt = successAt,
            CreatedAt = roundStart,
            UpdatedAt = successAt
        });
        await db.SaveChangesAsync();

        var engine = CreateEngine(db);
        await engine.CalculateRoundScoreAsync(competitionId, 1);
        await engine.CalculateRoundScoreAsync(competitionId, 2);

        var scores = await db.AwdpRoundScores
            .IgnoreQueryFilters()
            .OrderBy(s => s.RoundNumber)
            .ToListAsync();
        Assert.Equal(2, scores.Count);
        Assert.Equal(0, scores[0].RoundScoreDelta);
        Assert.Contains("break_success_next_round", scores[0].Reason);
        Assert.Contains("fix_success_next_round", scores[0].Reason);
        Assert.Equal(200, scores[1].RoundScoreDelta);

        var scoreEvent = await db.ScoreEvents.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(ScoringKeys.AwdpRound, scoreEvent.ScoringKey);
        Assert.Equal(200, scoreEvent.PointsDelta);
        Assert.Equal(2, scoreEvent.RoundNumber);
    }

    [Fact]
    public async Task CalculateRoundScoreAsync_FixFailed_DoesNotApplyDefaultPenalty()
    {
        var competitionId = Guid.NewGuid();
        var roundStart = DateTime.UtcNow;
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        var teamId = SeedTeam(db, competitionId);
        var challengeId = SeedChallenge(db, competitionId, attackScore: 70, defenseScore: 130);
        SeedRound(db, competitionId, 1, roundStart);
        db.AwdpTeamChallengeStates.Add(new AwdpTeamChallengeState
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            FixStatus = AwdpFixStatus.FixFailed,
            ServiceStatus = AwdpServiceStatus.ServiceOk,
            CreatedAt = roundStart,
            UpdatedAt = roundStart
        });
        await db.SaveChangesAsync();

        var engine = CreateEngine(db);
        await engine.CalculateRoundScoreAsync(competitionId, 1);

        var score = await db.AwdpRoundScores.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(0, score.DefenseScoreDelta);
        Assert.Equal(0, score.PenaltyDelta);
        Assert.Equal(0, score.RoundScoreDelta);
        Assert.Contains("fix_failed_no_penalty", score.Reason);
        Assert.Empty(await db.ScoreEvents.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task CalculateRoundScoreAsync_AppliesSolveDecayToAttackAndDefenseRoundScores()
    {
        var competitionId = Guid.NewGuid();
        var roundStart = DateTime.UtcNow;
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        var firstTeamId = SeedTeam(db, competitionId);
        var secondTeamId = SeedTeam(db, competitionId);
        var challengeId = SeedChallenge(
            db,
            competitionId,
            attackScore: 100,
            defenseScore: 80,
            pointsConfig: new PointsConfig(InitialPoints: 500, MinimumPoints: 10, DecayFactor: 1, DecayFunction: "linear"));
        SeedRound(db, competitionId, 1, roundStart);

        foreach (var teamId in new[] { firstTeamId, secondTeamId })
        {
            db.AwdpTeamChallengeStates.Add(new AwdpTeamChallengeState
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                TeamId = teamId,
                ChallengeId = challengeId,
                BreakStatus = AwdpBreakStatus.BreakSuccess,
                FixStatus = AwdpFixStatus.FixSuccess,
                BreakSucceededAt = roundStart.AddSeconds(-1),
                FixSucceededAt = roundStart.AddSeconds(-1),
                CreatedAt = roundStart.AddMinutes(-1),
                UpdatedAt = roundStart.AddSeconds(-1)
            });
        }

        await db.SaveChangesAsync();

        var engine = CreateEngine(db);
        await engine.CalculateRoundScoreAsync(competitionId, 1);

        var scores = await db.AwdpRoundScores
            .IgnoreQueryFilters()
            .OrderBy(s => s.TeamId)
            .ToListAsync();
        Assert.Equal(2, scores.Count);
        Assert.All(scores, score =>
        {
            Assert.Equal(10, score.AttackScoreDelta);
            Assert.Equal(10, score.DefenseScoreDelta);
            Assert.Equal(20, score.RoundScoreDelta);
        });
    }

    [Fact]
    public async Task CalculateRoundScoreAsync_DecayCountsOnlyRoundEffectiveSuccesses()
    {
        var competitionId = Guid.NewGuid();
        var roundStart = DateTime.UtcNow;
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        var earlyTeamId = SeedTeam(db, competitionId);
        var lateTeamId = SeedTeam(db, competitionId);
        var challengeId = SeedChallenge(
            db,
            competitionId,
            attackScore: 100,
            defenseScore: 80,
            pointsConfig: new PointsConfig(InitialPoints: 500, MinimumPoints: 10, DecayFactor: 1, DecayFunction: "linear"));
        SeedRound(db, competitionId, 1, roundStart);

        db.AwdpTeamChallengeStates.AddRange(
            new AwdpTeamChallengeState
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                TeamId = earlyTeamId,
                ChallengeId = challengeId,
                BreakStatus = AwdpBreakStatus.BreakSuccess,
                FixStatus = AwdpFixStatus.FixSuccess,
                BreakSucceededAt = roundStart.AddSeconds(-1),
                FixSucceededAt = roundStart.AddSeconds(-1),
                CreatedAt = roundStart.AddMinutes(-1),
                UpdatedAt = roundStart.AddSeconds(-1)
            },
            new AwdpTeamChallengeState
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                TeamId = lateTeamId,
                ChallengeId = challengeId,
                BreakStatus = AwdpBreakStatus.BreakSuccess,
                FixStatus = AwdpFixStatus.FixSuccess,
                BreakSucceededAt = roundStart.AddSeconds(30),
                FixSucceededAt = roundStart.AddSeconds(30),
                CreatedAt = roundStart,
                UpdatedAt = roundStart.AddSeconds(30)
            });
        await db.SaveChangesAsync();

        var engine = CreateEngine(db);
        await engine.CalculateRoundScoreAsync(competitionId, 1);

        var earlyScore = await db.AwdpRoundScores
            .IgnoreQueryFilters()
            .SingleAsync(s => s.TeamId == earlyTeamId);
        var lateScore = await db.AwdpRoundScores
            .IgnoreQueryFilters()
            .SingleAsync(s => s.TeamId == lateTeamId);

        Assert.Equal(100, earlyScore.AttackScoreDelta);
        Assert.Equal(80, earlyScore.DefenseScoreDelta);
        Assert.Equal(180, earlyScore.RoundScoreDelta);
        Assert.Equal(0, lateScore.RoundScoreDelta);
        Assert.Contains("break_success_next_round", lateScore.Reason);
        Assert.Contains("fix_success_next_round", lateScore.Reason);
    }

    private static void SeedCompetition(
        ApplicationDbContext db,
        Guid competitionId)
    {
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "AWDP Score Test",
            OwnerId = Guid.NewGuid(),
            GameModeType = GameModeType.Awdp,
            ModeKey = "awdp",
            StartTime = DateTime.UtcNow.AddMinutes(-1),
            EndTime = DateTime.UtcNow.AddHours(2),
            Status = CompetitionStatus.Running
        });
    }

    private static Guid SeedTeam(ApplicationDbContext db, Guid competitionId)
    {
        var id = Guid.NewGuid();
        db.Teams.Add(new Team
        {
            Id = id,
            CompetitionId = competitionId,
            Name = "Blue",
            CaptainId = Guid.NewGuid(),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            CreatedAt = DateTime.UtcNow
        });
        return id;
    }

    private static Guid SeedChallenge(
        ApplicationDbContext db,
        Guid competitionId,
        int attackScore = 70,
        int defenseScore = 130,
        PointsConfig? pointsConfig = null)
    {
        var id = Guid.NewGuid();
        db.Challenges.Add(new Challenge
        {
            Id = id,
            CompetitionId = competitionId,
            Title = "Web",
            TypeId = "awdp",
            PointsConfig = pointsConfig ?? new PointsConfig(),
            AwdpAttackScorePerRound = attackScore,
            AwdpDefenseScorePerRound = defenseScore,
            CreatedAt = DateTime.UtcNow
        });
        return id;
    }

    private static void SeedRound(ApplicationDbContext db, Guid competitionId, int roundNumber, DateTime startTime)
    {
        db.AwdpRounds.Add(new AwdpRound
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            RoundNumber = roundNumber,
            StartTime = startTime,
            Status = AwdpRoundStatus.RoundRunning
        });
    }

    private sealed class NullLeaderboardServiceAwdp : ILeaderboardService
    {
        public Task<IReadOnlyList<LeaderboardEntry>> CalculateLeaderboardAsync(Guid competitionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LeaderboardEntry>>(Array.Empty<LeaderboardEntry>());
    }

    private sealed class NullHubNotifierAwdp : IHubNotifierService
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

file class FixedTenantContextAwdpScore(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
