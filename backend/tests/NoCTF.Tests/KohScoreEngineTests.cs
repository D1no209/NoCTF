using Microsoft.EntityFrameworkCore;
using NoCTF.Application;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Plugins.KoH;

namespace NoCTF.Tests;

/// <summary>
/// Unit tests for KohScoreEngine — KoH control-record tracking and interval scoring.
/// </summary>
public class KohScoreEngineTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContextKohEngine(competitionId));
    }

    private static KohScoreEngine CreateEngine(ApplicationDbContext db, KohHubNotifierSpy? notifier = null)
        => new(db, new NullLeaderboardKoh(), new NullRedisLeaderboardCache(), notifier ?? new KohHubNotifierSpy(), CreateScoreSignalEmitter(db));

    private static IScoreSignalEmitter CreateScoreSignalEmitter(ApplicationDbContext db)
    {
        var writer = new ScoreEventWriter(db);
        IScoringStrategy[] strategies = [new ControlIntervalScoringStrategy(db, writer)];
        return new ScoreSignalEmitter(
            db,
            strategies,
            new CompetitionScoringProfileResolver(db),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ScoreSignalEmitter>.Instance);
    }

    private static void SeedCompetition(ApplicationDbContext db, Guid competitionId, int controlPoints = 10)
    {
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "KoH",
            OwnerId = Guid.NewGuid(),
            GameModeType = GameModeType.Koh,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(2),
            Status = CompetitionStatus.Running,
            ControlPointsPerInterval = controlPoints
        });
        db.SaveChanges();
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
        db.SaveChanges();
        return id;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Same team controls for 2 consecutive ticks after initial capture → 2 ScoreEvents.
    /// </summary>
    [Fact]
    public async Task UpdateControlAsync_SameController_AwardsPoints()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId, controlPoints: 15);
        var teamId = SeedTeam(db, competitionId, "Team A");
        var challengeId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var engine = CreateEngine(db);

        // Tick 1: null → teamId (controller change, no points)
        await engine.UpdateControlAsync(competitionId, challengeId, teamId, now, 15);
        // Tick 2: same controller → +15 points
        await engine.UpdateControlAsync(competitionId, challengeId, teamId, now.AddSeconds(30), 15);
        // Tick 3: same controller → +15 points
        await engine.UpdateControlAsync(competitionId, challengeId, teamId, now.AddSeconds(60), 15);

        var events = await db.ScoreEvents.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, e =>
        {
            Assert.Equal(teamId, e.TeamId);
            Assert.Equal(15, e.PointsDelta);
            Assert.Equal("koh.control-interval", e.EventType);
        });
    }

    /// <summary>
    /// Team A then Team B → A's record has EndTime, B's record has no EndTime.
    /// </summary>
    [Fact]
    public async Task UpdateControlAsync_ControllerChange_EndsOldRecordStartsNew()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        var teamA = SeedTeam(db, competitionId, "Team A");
        var teamB = SeedTeam(db, competitionId, "Team B");
        var challengeId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;
        var t1 = t0.AddSeconds(30);

        var engine = CreateEngine(db);

        await engine.UpdateControlAsync(competitionId, challengeId, teamA, t0, 10);
        await engine.UpdateControlAsync(competitionId, challengeId, teamB, t1, 10);

        var records = await db.KohControlRecords.IgnoreQueryFilters()
            .OrderBy(r => r.StartTime).ToListAsync();

        Assert.Equal(2, records.Count);

        var aRecord = records.Single(r => r.TeamId == teamA);
        Assert.NotNull(aRecord.EndTime);
        Assert.Equal(t1, aRecord.EndTime);

        var bRecord = records.Single(r => r.TeamId == teamB);
        Assert.Null(bRecord.EndTime);
        Assert.Equal(t1, bRecord.StartTime);
    }

    /// <summary>
    /// Active record exists, new controller is null → active record gets EndTime, no new record, no crash.
    /// </summary>
    [Fact]
    public async Task UpdateControlAsync_NoController_DoesNotCrash()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        var teamA = SeedTeam(db, competitionId, "Team A");
        var challengeId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;
        var t1 = t0.AddSeconds(30);

        var engine = CreateEngine(db);

        // Team A takes control
        await engine.UpdateControlAsync(competitionId, challengeId, teamA, t0, 10);
        // Control lost
        await engine.UpdateControlAsync(competitionId, challengeId, null, t1, 10);

        var records = await db.KohControlRecords.IgnoreQueryFilters().ToListAsync();
        Assert.Single(records);
        Assert.NotNull(records[0].EndTime);
        Assert.Equal(t1, records[0].EndTime);

        // No new record for null controller
        Assert.DoesNotContain(records, r => r.EndTime == null);
    }

    /// <summary>
    /// NotifyKohUpdateAsync is called when controller changes.
    /// </summary>
    [Fact]
    public async Task UpdateControlAsync_SignalR_NotifiedOnChange()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        var teamA = SeedTeam(db, competitionId, "Team A");
        var teamB = SeedTeam(db, competitionId, "Team B");
        var challengeId = Guid.NewGuid();
        var spy = new KohHubNotifierSpy();

        var engine = CreateEngine(db, spy);

        // null → A: 1 notification
        await engine.UpdateControlAsync(competitionId, challengeId, teamA, DateTime.UtcNow, 10);
        Assert.Equal(1, spy.KohUpdateCount);

        // A → A: no notification (same controller)
        await engine.UpdateControlAsync(competitionId, challengeId, teamA, DateTime.UtcNow.AddSeconds(30), 10);
        Assert.Equal(1, spy.KohUpdateCount);

        // A → B: 1 more notification
        await engine.UpdateControlAsync(competitionId, challengeId, teamB, DateTime.UtcNow.AddSeconds(60), 10);
        Assert.Equal(2, spy.KohUpdateCount);
    }

    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class NullLeaderboardKoh : ILeaderboardService
    {
        public Task<IReadOnlyList<LeaderboardEntry>> CalculateLeaderboardAsync(
            Guid competitionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LeaderboardEntry>>(Array.Empty<LeaderboardEntry>());
    }

    private sealed class KohHubNotifierSpy : IHubNotifierService
    {
        public int KohUpdateCount { get; private set; }

        public Task NotifyKohUpdateAsync(Guid competitionId, Guid challengeId, Guid? controllerTeamId, DateTime timestamp, CancellationToken ct = default)
        {
            KohUpdateCount++;
            return Task.CompletedTask;
        }

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
    }
}

file class FixedTenantContextKohEngine(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
