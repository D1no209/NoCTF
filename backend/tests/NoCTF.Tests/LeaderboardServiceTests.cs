using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Tests;

/// <summary>
/// Tests for LeaderboardService tie-breaker logic and score aggregation.
/// Uses EF Core InMemory provider to avoid a real database.
/// </summary>
public class LeaderboardServiceTests
{
    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        // Tenant context that matches the competition
        var tenantContext = new FixedTenantContext(competitionId);
        return new ApplicationDbContext(options, tenantContext);
    }

    [Fact]
    public async Task CalculateLeaderboard_SingleTeam_ReturnsRank1()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedTeam(db, competitionId, teamId, "Alpha");
        SeedChallenge(db, competitionId, challengeId);
        SeedScoreEvent(db, competitionId, teamId, 500);
        SeedCorrectSubmission(db, competitionId, teamId, challengeId, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var service = new LeaderboardService(db);
        var result = await service.CalculateLeaderboardAsync(competitionId);

        Assert.Single(result);
        Assert.Equal(1, result[0].Rank);
        Assert.Equal(500, result[0].TotalScore);
        Assert.Equal(1, result[0].SolvedCount);
    }

    [Fact]
    public async Task CalculateLeaderboard_HigherScoreRanksFirst()
    {
        var competitionId = Guid.NewGuid();
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedTeam(db, competitionId, teamA, "Alpha");
        SeedTeam(db, competitionId, teamB, "Beta");
        SeedScoreEvent(db, competitionId, teamA, 300);
        SeedScoreEvent(db, competitionId, teamB, 700);
        await db.SaveChangesAsync();

        var service = new LeaderboardService(db);
        var result = await service.CalculateLeaderboardAsync(competitionId);

        Assert.Equal(2, result.Count);
        Assert.Equal(teamB, result[0].TeamId); // Beta has 700
        Assert.Equal(teamA, result[1].TeamId); // Alpha has 300
    }

    [Fact]
    public async Task CalculateLeaderboard_TieBreaker_EarlierFirstSolveRanksHigher()
    {
        var competitionId = Guid.NewGuid();
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedTeam(db, competitionId, teamA, "Alpha");
        SeedTeam(db, competitionId, teamB, "Beta");
        SeedChallenge(db, competitionId, challengeId);

        // Both teams have 500 points
        SeedScoreEvent(db, competitionId, teamA, 500);
        SeedScoreEvent(db, competitionId, teamB, 500);

        // Alpha solved earlier
        var earlier = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var later = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        SeedCorrectSubmission(db, competitionId, teamA, challengeId, earlier);
        SeedCorrectSubmission(db, competitionId, teamB, challengeId, later);
        await db.SaveChangesAsync();

        var service = new LeaderboardService(db);
        var result = await service.CalculateLeaderboardAsync(competitionId);

        Assert.Equal(2, result.Count);
        Assert.Equal(teamA, result[0].TeamId); // Alpha solved earlier → rank 1
        Assert.Equal(teamB, result[1].TeamId);
        Assert.Equal(1, result[0].Rank);
        Assert.Equal(2, result[1].Rank);
    }

    [Fact]
    public async Task CalculateLeaderboard_MultipleScoreEvents_SumsCorrectly()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedTeam(db, competitionId, teamId, "Alpha");
        SeedScoreEvent(db, competitionId, teamId, 300);
        SeedScoreEvent(db, competitionId, teamId, 200);
        SeedScoreEvent(db, competitionId, teamId, -50); // penalty/correction
        await db.SaveChangesAsync();

        var service = new LeaderboardService(db);
        var result = await service.CalculateLeaderboardAsync(competitionId);

        Assert.Single(result);
        Assert.Equal(450, result[0].TotalScore); // 300 + 200 - 50
    }

    [Fact]
    public async Task CalculateLeaderboard_SolvedCountIsDistinctChallenges()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var secondChallengeId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedTeam(db, competitionId, teamId, "Alpha");
        SeedChallenge(db, competitionId, challengeId);
        SeedChallenge(db, competitionId, secondChallengeId, "webwarmup", "WEB");
        SeedScoreEvent(db, competitionId, teamId, 500);

        // Two correct submissions for the same challenge (should count as 1 solve)
        SeedCorrectSubmission(db, competitionId, teamId, challengeId, DateTime.UtcNow.AddMinutes(-10));
        SeedCorrectSubmission(db, competitionId, teamId, challengeId, DateTime.UtcNow);
        // One for a different challenge
        SeedCorrectSubmission(db, competitionId, teamId, secondChallengeId, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var service = new LeaderboardService(db);
        var result = await service.CalculateLeaderboardAsync(competitionId);

        Assert.Single(result);
        Assert.Equal(2, result[0].SolvedCount); // 2 distinct challenges
    }

    [Fact]
    public async Task CalculateLeaderboard_EmptyCompetition_ReturnsEmpty()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        var service = new LeaderboardService(db);
        var result = await service.CalculateLeaderboardAsync(competitionId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task CalculateLeaderboard_RegisteredTeamsWithoutScores_AreIncluded()
    {
        var competitionId = Guid.NewGuid();
        var scoringTeam = Guid.NewGuid();
        var zeroTeam = Guid.NewGuid();
        var rejectedTeam = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedTeam(db, competitionId, scoringTeam, "Alpha");
        SeedTeam(db, competitionId, zeroTeam, "Beta");
        SeedTeam(db, competitionId, rejectedTeam, "Rejected", TeamRegistrationStatus.Rejected);
        SeedScoreEvent(db, competitionId, scoringTeam, 300);
        await db.SaveChangesAsync();

        var service = new LeaderboardService(db);
        var result = await service.CalculateLeaderboardAsync(competitionId);

        Assert.Equal(2, result.Count);
        Assert.Equal(scoringTeam, result[0].TeamId);
        Assert.Equal(zeroTeam, result[1].TeamId);
        Assert.Equal(0, result[1].TotalScore);
        Assert.DoesNotContain(result, e => e.TeamId == rejectedTeam);
    }

    [Fact]
    public async Task CalculateLeaderboard_BannedTeams_AreExcluded()
    {
        var competitionId = Guid.NewGuid();
        var activeTeam = Guid.NewGuid();
        var bannedTeam = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedTeam(db, competitionId, activeTeam, "Alpha");
        SeedTeam(db, competitionId, bannedTeam, "Banned", isBanned: true);
        SeedScoreEvent(db, competitionId, activeTeam, 100);
        SeedScoreEvent(db, competitionId, bannedTeam, 900);
        await db.SaveChangesAsync();

        var service = new LeaderboardService(db);
        var result = await service.CalculateLeaderboardAsync(competitionId);

        Assert.Single(result);
        Assert.Equal(activeTeam, result[0].TeamId);
        Assert.DoesNotContain(result, e => e.TeamId == bannedTeam);
    }

    [Fact]
    public async Task BuildTeamDetail_BannedTeam_ReturnsNull()
    {
        var competitionId = Guid.NewGuid();
        var bannedTeam = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedTeam(db, competitionId, bannedTeam, "Banned", isBanned: true);
        SeedScoreEvent(db, competitionId, bannedTeam, 900);
        await db.SaveChangesAsync();

        var leaderboard = new LeaderboardService(db);
        var insight = new LeaderboardInsightService(db, leaderboard);
        var result = await insight.BuildTeamDetailAsync(competitionId, bannedTeam);

        Assert.Null(result);
    }

    [Fact]
    public async Task BuildTeamDetail_ExcludesScoresForDeletedChallenges()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var activeChallengeId = Guid.NewGuid();
        var orphanedChallengeId = Guid.NewGuid();
        var solvedAt = new DateTime(2026, 7, 4, 14, 5, 0, DateTimeKind.Utc);
        await using var db = CreateDb(competitionId);

        SeedTeam(db, competitionId, teamId, "Alpha");
        var player = new User
        {
            Id = userId,
            UserName = "player",
            Email = "player@example.com",
            PasswordHash = "hash",
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(player);
        db.Entry(player).Property<string>("NormalizedUserName").CurrentValue = "player";
        db.Entry(player).Property<string>("NormalizedEmail").CurrentValue = "player@example.com";
        db.TeamMembers.Add(new TeamMember
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            UserId = userId,
            Role = TeamMemberRole.Captain,
            JoinedAt = DateTime.UtcNow
        });
        db.Challenges.Add(new Challenge
        {
            Id = activeChallengeId,
            CompetitionId = competitionId,
            Title = "babystack",
            TypeId = "PWN",
            PointsConfig = new PointsConfig(InitialPoints: 500, MinimumPoints: 100, DecayFactor: 450),
            DifficultyCoefficient = 1,
            CreatedAt = DateTime.UtcNow
        });
        SeedCorrectSubmission(db, competitionId, teamId, orphanedChallengeId, solvedAt, userId);
        db.ScoreEvents.Add(new ScoreEvent
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = orphanedChallengeId,
            ScoringKey = ScoringKeys.DecaySolve,
            EventType = "FlagSolved",
            PointsDelta = 500,
            Reason = "Solved challenge 'babystack'",
            Timestamp = solvedAt
        });
        await db.SaveChangesAsync();

        var leaderboard = new LeaderboardService(db);
        var leaderboardRows = await leaderboard.CalculateLeaderboardAsync(competitionId);
        var insight = new LeaderboardInsightService(db, leaderboard);
        var result = await insight.BuildTeamDetailAsync(competitionId, teamId);

        Assert.Single(leaderboardRows);
        Assert.Equal(0, leaderboardRows[0].TotalScore);
        Assert.Equal(0, leaderboardRows[0].SolvedCount);
        Assert.NotNull(result);
        Assert.Equal(0, result!.TotalScore);
        Assert.Equal(0, result.SolvedCount);
        Assert.Empty(result.DirectionScores);
        Assert.Empty(Assert.Single(result.Members).Solves);

        var challengeScore = Assert.Single(result.ChallengeScores);
        Assert.Equal(activeChallengeId, challengeScore.ChallengeId);
        Assert.Equal("babystack", challengeScore.ChallengeTitle);
        Assert.Equal("PWN", challengeScore.Direction);
        Assert.Equal(0, challengeScore.TotalScore);
        Assert.Null(challengeScore.SolvedAt);
    }

    [Fact]
    public async Task BuildTeamDetail_WhenMembersAreNotRequested_SkipsMemberProjection()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedTeam(db, competitionId, teamId, "Alpha");
        var privateMember = new User
        {
            Id = userId,
            UserName = "private-member",
            Email = "private-member@example.com",
            PasswordHash = "hash",
            Role = UserRole.User,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(privateMember);
        db.Entry(privateMember).Property<string>("NormalizedUserName").CurrentValue = "private-member";
        db.Entry(privateMember).Property<string>("NormalizedEmail").CurrentValue = "private-member@example.com";
        db.TeamMembers.Add(new TeamMember
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            UserId = userId,
            Role = TeamMemberRole.Member,
            JoinedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var insight = new LeaderboardInsightService(db, new LeaderboardService(db));

        var result = await insight.BuildTeamDetailAsync(
            competitionId,
            teamId,
            includeMembers: false);

        Assert.NotNull(result);
        Assert.Empty(result!.Members);
    }

    [Fact]
    public async Task BuildTrend_ExtendsTimelineToEndedCompetitionEnd()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var startTime = new DateTime(2026, 7, 4, 10, 0, 0, DateTimeKind.Utc);
        var solvedAt = startTime.AddMinutes(20);
        var endTime = startTime.AddHours(2);
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId, startTime, endTime);
        SeedTeam(db, competitionId, teamId, "Alpha");
        SeedChallenge(db, competitionId, challengeId);
        db.ScoreEvents.Add(new ScoreEvent
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            ScoringKey = ScoringKeys.DecaySolve,
            EventType = "ctf.solve",
            PointsDelta = 500,
            Timestamp = solvedAt
        });
        await db.SaveChangesAsync();

        var leaderboard = new LeaderboardService(db);
        var insight = new LeaderboardInsightService(db, leaderboard);
        var result = await insight.BuildTrendAsync(competitionId);

        var points = Assert.Single(result.Series).Points;
        Assert.Equal(startTime, points[0].Timestamp);
        Assert.Equal(endTime, points[^1].Timestamp);
        Assert.Equal(500, points[^1].Score);
    }

    [Fact]
    public async Task BuildTeamDetail_AggregatesPluginScoringKeys_AndComputesOnlyTargetRanks()
    {
        var competitionId = Guid.NewGuid();
        var targetTeamId = Guid.NewGuid();
        var earlierTeamId = Guid.NewGuid();
        var laterTeamId = Guid.NewGuid();
        var bannedTeamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var targetSolvedAt = new DateTime(2026, 7, 4, 12, 0, 0, DateTimeKind.Utc);
        await using var db = CreateDb(competitionId);

        SeedTeam(db, competitionId, targetTeamId, "Target");
        SeedTeam(db, competitionId, earlierTeamId, "Earlier");
        SeedTeam(db, competitionId, laterTeamId, "Later");
        SeedTeam(db, competitionId, bannedTeamId, "Banned", isBanned: true);
        SeedChallenge(db, competitionId, challengeId);

        SeedCorrectSubmission(db, competitionId, earlierTeamId, challengeId, targetSolvedAt.AddMinutes(-1));
        SeedCorrectSubmission(db, competitionId, targetTeamId, challengeId, targetSolvedAt);
        SeedCorrectSubmission(db, competitionId, laterTeamId, challengeId, targetSolvedAt.AddMinutes(1));
        SeedCorrectSubmission(db, competitionId, bannedTeamId, challengeId, targetSolvedAt.AddMinutes(-2));

        SeedChallengeScoreEvent(db, competitionId, targetTeamId, challengeId, ScoringKeys.DecaySolve, 300, targetSolvedAt);
        SeedChallengeScoreEvent(db, competitionId, targetTeamId, challengeId, ScoringKeys.BloodBonus, 50, targetSolvedAt);
        SeedChallengeScoreEvent(db, competitionId, targetTeamId, challengeId, ScoringKeys.PenetrationStage, 200, targetSolvedAt);
        SeedChallengeScoreEvent(db, competitionId, targetTeamId, challengeId, ScoringKeys.PenetrationBloodBonus, 75, targetSolvedAt);
        SeedChallengeScoreEvent(db, competitionId, targetTeamId, challengeId, ScoringKeys.RoundAccumulation, -25, targetSolvedAt);
        await db.SaveChangesAsync();

        var insight = new LeaderboardInsightService(db, new LeaderboardService(db));
        var result = await insight.BuildTeamDetailAsync(
            competitionId,
            targetTeamId,
            includeMembers: false);

        Assert.NotNull(result);
        Assert.Equal(600, result!.TotalScore);
        var challengeScore = Assert.Single(result.ChallengeScores);
        Assert.Equal(475, challengeScore.BaseScore);
        Assert.Equal(125, challengeScore.BonusScore);
        Assert.Equal(600, challengeScore.TotalScore);
        Assert.Equal(2, challengeScore.BloodRank);
        Assert.Equal(targetSolvedAt, challengeScore.SolvedAt);
        Assert.Equal(
            CtfScoreCalculator.CalculateChallengePoints(
                3,
                new PointsConfig(InitialPoints: 500, MinimumPoints: 100, DecayFactor: 450),
                1),
            challengeScore.CurrentPoints);
    }

    [Fact]
    public void BuildTeamDetail_RankAndSolveCountQueries_TranslateForPostgres()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=noctf-query-translation;Username=noctf;Password=noctf")
            .Options;
        using var db = new ApplicationDbContext(options, new FixedTenantContext(competitionId));

        var approvedSubmissions = LeaderboardInsightService.BuildApprovedCorrectSubmissionsQuery(
            db,
            competitionId,
            [Guid.NewGuid(), Guid.NewGuid()]);
        var countSql = approvedSubmissions
            .GroupBy(solve => solve.ChallengeId)
            .Select(group => new
            {
                ChallengeId = group.Key,
                Count = group.Select(solve => solve.TeamId).Distinct().Count()
            })
            .ToQueryString();
        var rankSql = LeaderboardInsightService
            .BuildChallengeSolveRanksQuery(approvedSubmissions, teamId)
            .ToQueryString();

        Assert.Contains("GROUP BY", countSql);
        Assert.Contains("GROUP BY", rankSql);
        Assert.Contains("count", rankSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildTrend_GroupedTopNQuery_TranslatesForPostgres()
    {
        var competitionId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=noctf-query-translation;Username=noctf;Password=noctf")
            .Options;
        using var db = new ApplicationDbContext(options, new FixedTenantContext(competitionId));

        var sql = LeaderboardInsightService.BuildRecentTrendEventsQuery(
                db,
                competitionId,
                [Guid.NewGuid(), Guid.NewGuid()],
                DateTime.UtcNow.AddHours(-1),
                DateTime.UtcNow)
            .ToQueryString();

        Assert.Contains("ROW_NUMBER", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PARTITION BY", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildTrend_LargeHistory_IsBoundedAndDeterministic()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var startTime = new DateTime(2026, 7, 4, 10, 0, 0, DateTimeKind.Utc);
        var endTime = startTime.AddHours(1);
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId, startTime, endTime);
        SeedTeam(db, competitionId, teamId, "Alpha");
        SeedChallenge(db, competitionId, challengeId);
        for (var index = 1; index <= 2500; index++)
        {
            SeedChallengeScoreEvent(
                db,
                competitionId,
                teamId,
                challengeId,
                ScoringKeys.RoundAccumulation,
                1,
                startTime.AddSeconds(index));
        }
        await db.SaveChangesAsync();

        var insight = new LeaderboardInsightService(db, new LeaderboardService(db));
        var first = Assert.Single((await insight.BuildTrendAsync(competitionId)).Series);
        var second = Assert.Single((await insight.BuildTrendAsync(competitionId)).Series);

        Assert.InRange(first.Points.Count, 2, 256);
        Assert.Equal(startTime, first.Points[0].Timestamp);
        Assert.Equal(452, first.Points[0].Score);
        Assert.Equal(endTime, first.Points[^1].Timestamp);
        Assert.Equal(2500, first.Points[^1].Score);
        Assert.Equal(first.Points, second.Points);
        Assert.True(first.Points.Zip(first.Points.Skip(1)).All(pair =>
            pair.First.Timestamp <= pair.Second.Timestamp));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void SeedCompetition(
        ApplicationDbContext db,
        Guid competitionId,
        DateTime startTime,
        DateTime endTime)
    {
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "CCC",
            GameModeType = GameModeType.Ctf,
            ModeKey = "ctf",
            OwnerId = Guid.NewGuid(),
            StartTime = startTime,
            EndTime = endTime,
            Status = CompetitionStatus.Finished
        });
    }

    private static void SeedTeam(
        ApplicationDbContext db,
        Guid competitionId,
        Guid teamId,
        string name,
        TeamRegistrationStatus status = TeamRegistrationStatus.Approved,
        bool isBanned = false)
    {
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = name,
            CaptainId = Guid.NewGuid(),
            RegistrationStatus = status,
            IsBanned = isBanned,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static void SeedScoreEvent(ApplicationDbContext db, Guid competitionId, Guid teamId, int points)
    {
        db.ScoreEvents.Add(new ScoreEvent
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            EventType = "FlagSolved",
            PointsDelta = points,
            Timestamp = DateTime.UtcNow
        });
    }

    private static void SeedChallengeScoreEvent(
        ApplicationDbContext db,
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        string scoringKey,
        int points,
        DateTime timestamp)
    {
        db.ScoreEvents.Add(new ScoreEvent
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            ScoringKey = scoringKey,
            EventType = "test.score",
            PointsDelta = points,
            Timestamp = timestamp
        });
    }

    private static void SeedChallenge(
        ApplicationDbContext db,
        Guid competitionId,
        Guid challengeId,
        string title = "babystack",
        string typeId = "PWN")
    {
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            CompetitionId = competitionId,
            Title = title,
            TypeId = typeId,
            PointsConfig = new PointsConfig(InitialPoints: 500, MinimumPoints: 100, DecayFactor: 450),
            DifficultyCoefficient = 1,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static void SeedCorrectSubmission(
        ApplicationDbContext db,
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        DateTime submittedAt,
        Guid? userId = null)
    {
        db.Submissions.Add(new Submission
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            UserId = userId ?? Guid.NewGuid(),
            FlagContent = "flag{test}",
            IsCorrect = true,
            SubmittedAt = submittedAt,
            IpAddress = "127.0.0.1"
        });
    }
}

/// <summary>Fixed tenant context for tests.</summary>
file class FixedTenantContext(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { /* no-op for tests */ }
}
