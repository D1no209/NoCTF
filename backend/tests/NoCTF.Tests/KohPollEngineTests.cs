using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Plugins.KoH;

namespace NoCTF.Tests;

/// <summary>
/// Tests for KohScoreEngine (the core KoH control-record and scoring logic).
/// </summary>
public class KohPollEngineTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContextKoh(competitionId));
    }

    private static KohScoreEngine CreateEngine(ApplicationDbContext db, NullKohHubNotifier? notifier = null)
        => new(db, new NullLeaderboardServiceKoh(), new NullRedisLeaderboardCache(), notifier ?? new NullKohHubNotifier(), CreateScoreSignalEmitter(db));

    private static IScoreSignalEmitter CreateScoreSignalEmitter(ApplicationDbContext db)
    {
        var writer = new ScoreEventWriter(db);
        IScoringStrategy[] strategies = [new ControlIntervalScoringStrategy(db, writer)];
        return new ScoreSignalEmitter(
            db,
            strategies,
            new CompetitionScoringProfileResolver(db),
            NullLogger<ScoreSignalEmitter>.Instance);
    }

    private static Guid SeedCompetition(ApplicationDbContext db, Guid? competitionId = null, int controlPoints = 10)
    {
        var id = competitionId ?? Guid.NewGuid();
        db.Competitions.Add(new Competition
        {
            Id = id,
            CompetitionId = id,
            Title = "KoH Test",
            OwnerId = Guid.NewGuid(),
            GameModeType = GameModeType.Koh,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(2),
            Status = CompetitionStatus.Running,
            ControlPointsPerInterval = controlPoints
        });
        db.SaveChanges();
        return id;
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
    /// Same controller across 3 intervals → 3 ScoreEvents awarded.
    /// </summary>
    [Fact]
    public async Task SameController_ThreeIntervals_ThreeScoreEvents()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId, controlPoints: 10);
        var teamId = SeedTeam(db, competitionId, "Team A");
        var challengeId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var engine = CreateEngine(db);

        // First poll: no active record → controller change (new record created, no points yet)
        await engine.UpdateControlAsync(competitionId, challengeId, teamId, now, 10);

        // Intervals 2, 3, 4: same controller → points awarded each time
        await engine.UpdateControlAsync(competitionId, challengeId, teamId, now.AddSeconds(30), 10);
        await engine.UpdateControlAsync(competitionId, challengeId, teamId, now.AddSeconds(60), 10);
        await engine.UpdateControlAsync(competitionId, challengeId, teamId, now.AddSeconds(90), 10);

        var events = await db.ScoreEvents.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(3, events.Count);
        Assert.All(events, e =>
        {
            Assert.Equal(teamId, e.TeamId);
            Assert.Equal(10, e.PointsDelta);
            Assert.Equal("koh.control-interval", e.EventType);
        });
    }

    /// <summary>
    /// Controller switch → old record ended, new record started.
    /// </summary>
    [Fact]
    public async Task ControllerSwitch_OldRecordEnded_NewRecordStarted()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId, controlPoints: 10);
        var teamA = SeedTeam(db, competitionId, "Team A");
        var teamB = SeedTeam(db, competitionId, "Team B");
        var challengeId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;
        var t1 = t0.AddSeconds(30);

        var engine = CreateEngine(db);

        // Team A takes control
        await engine.UpdateControlAsync(competitionId, challengeId, teamA, t0, 10);

        // Team B takes over
        await engine.UpdateControlAsync(competitionId, challengeId, teamB, t1, 10);

        var records = await db.KohControlRecords.IgnoreQueryFilters()
            .OrderBy(r => r.StartTime)
            .ToListAsync();

        Assert.Equal(2, records.Count);

        // Old record (Team A) should be ended
        var teamARecord = records.First(r => r.TeamId == teamA);
        Assert.NotNull(teamARecord.EndTime);
        Assert.Equal(t1, teamARecord.EndTime);

        // New record (Team B) should be active
        var teamBRecord = records.First(r => r.TeamId == teamB);
        Assert.Null(teamBRecord.EndTime);
        Assert.Equal(t1, teamBRecord.StartTime);
    }

    /// <summary>
    /// Controller switch → SignalR KohUpdate notification fired.
    /// </summary>
    [Fact]
    public async Task ControllerSwitch_NotifiesKohUpdate()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId, controlPoints: 10);
        var teamA = SeedTeam(db, competitionId, "Team A");
        var teamB = SeedTeam(db, competitionId, "Team B");
        var challengeId = Guid.NewGuid();
        var notifier = new NullKohHubNotifier();

        var engine = CreateEngine(db, notifier);

        await engine.UpdateControlAsync(competitionId, challengeId, teamA, DateTime.UtcNow, 10);
        await engine.UpdateControlAsync(competitionId, challengeId, teamB, DateTime.UtcNow.AddSeconds(30), 10);

        // Two controller changes: null→A and A→B
        Assert.Equal(2, notifier.KohUpdateCount);
    }

    /// <summary>
    /// No controller (null) → no score events, no active record.
    /// </summary>
    [Fact]
    public async Task NoController_NoScoreEvents_NoActiveRecord()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId, controlPoints: 10);
        var challengeId = Guid.NewGuid();

        var engine = CreateEngine(db);

        await engine.UpdateControlAsync(competitionId, challengeId, null, DateTime.UtcNow, 10);
        await engine.UpdateControlAsync(competitionId, challengeId, null, DateTime.UtcNow.AddSeconds(30), 10);

        var events = await db.ScoreEvents.IgnoreQueryFilters().ToListAsync();
        Assert.Empty(events);

        var records = await db.KohControlRecords.IgnoreQueryFilters().ToListAsync();
        Assert.Empty(records);
    }

    /// <summary>
    /// Controller loses control (team → null) → active record ended.
    /// </summary>
    [Fact]
    public async Task ControllerLosesControl_ActiveRecordEnded()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId, controlPoints: 10);
        var teamA = SeedTeam(db, competitionId, "Team A");
        var challengeId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;
        var t1 = t0.AddSeconds(30);

        var engine = CreateEngine(db);

        await engine.UpdateControlAsync(competitionId, challengeId, teamA, t0, 10);
        await engine.UpdateControlAsync(competitionId, challengeId, null, t1, 10);

        var records = await db.KohControlRecords.IgnoreQueryFilters().ToListAsync();
        Assert.Single(records);
        Assert.NotNull(records[0].EndTime);
        Assert.Equal(t1, records[0].EndTime);
    }

    // ── Null stubs ────────────────────────────────────────────────────────────

    private sealed class NullLeaderboardServiceKoh : ILeaderboardService
    {
        public Task<IReadOnlyList<LeaderboardEntry>> CalculateLeaderboardAsync(
            Guid competitionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LeaderboardEntry>>(Array.Empty<LeaderboardEntry>());
    }

    private sealed class NullKohHubNotifier : IHubNotifierService
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

/// <summary>Fixed tenant context for KoH tests.</summary>
file class FixedTenantContextKoh(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { /* no-op */ }
}
