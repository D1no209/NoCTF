using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Leaderboard;
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
        await using var db = CreateDb(competitionId);

        SeedTeam(db, competitionId, teamId, "Alpha");
        SeedScoreEvent(db, competitionId, teamId, 500);
        SeedCorrectSubmission(db, competitionId, teamId, Guid.NewGuid(), DateTime.UtcNow);
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
        await using var db = CreateDb(competitionId);

        SeedTeam(db, competitionId, teamId, "Alpha");
        SeedScoreEvent(db, competitionId, teamId, 500);

        // Two correct submissions for the same challenge (should count as 1 solve)
        SeedCorrectSubmission(db, competitionId, teamId, challengeId, DateTime.UtcNow.AddMinutes(-10));
        SeedCorrectSubmission(db, competitionId, teamId, challengeId, DateTime.UtcNow);
        // One for a different challenge
        SeedCorrectSubmission(db, competitionId, teamId, Guid.NewGuid(), DateTime.UtcNow);
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

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void SeedTeam(ApplicationDbContext db, Guid competitionId, Guid teamId, string name)
    {
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = name,
            CaptainId = Guid.NewGuid(),
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

    private static void SeedCorrectSubmission(ApplicationDbContext db, Guid competitionId, Guid teamId, Guid challengeId, DateTime submittedAt)
    {
        db.Submissions.Add(new Submission
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            UserId = Guid.NewGuid(),
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
