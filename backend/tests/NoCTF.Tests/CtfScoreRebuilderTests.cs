using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Tests;

public class CtfScoreRebuilderTests
{
    [Fact]
    public async Task RebuildChallenge_ReflowsDecayForAllApprovedTeams()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        SeedChallenge(db, competitionId, challengeId);
        SeedTeam(db, competitionId, teamA, "a");
        SeedTeam(db, competitionId, teamB, "b");
        db.Submissions.AddRange(
            CorrectSubmission(competitionId, teamA, challengeId, DateTime.UtcNow.AddMinutes(-2)),
            CorrectSubmission(competitionId, teamB, challengeId, DateTime.UtcNow.AddMinutes(-1)));
        db.ScoreEvents.AddRange(
            ScoreEvent(competitionId, teamA, challengeId, 500),
            ScoreEvent(competitionId, teamB, challengeId, 500));
        await db.SaveChangesAsync();

        await new CtfScoreRebuilder(db).RebuildChallengeAsync(competitionId, challengeId);

        var expected = CtfScoreCalculator.CalculateChallengePoints(2, new PointsConfig());
        var events = await db.ScoreEvents.IgnoreQueryFilters()
            .Where(e => e.ScoringKey == ScoringKeys.DecaySolve)
            .OrderBy(e => e.TeamId)
            .ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(expected, e.PointsDelta));
        Assert.All(events, e => Assert.StartsWith("ctf-rebuild:", e.IdempotencyKey));
    }

    [Fact]
    public async Task RebuildCompetition_ExcludesBannedTeamsAndRestoresRemainingScores()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var bannedTeam = Guid.NewGuid();
        var activeTeam = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        SeedChallenge(db, competitionId, challengeId);
        SeedTeam(db, competitionId, bannedTeam, "banned", banned: true);
        SeedTeam(db, competitionId, activeTeam, "active");
        db.Submissions.AddRange(
            CorrectSubmission(competitionId, bannedTeam, challengeId, DateTime.UtcNow.AddMinutes(-2)),
            CorrectSubmission(competitionId, activeTeam, challengeId, DateTime.UtcNow.AddMinutes(-1)));
        db.ScoreEvents.AddRange(
            ScoreEvent(competitionId, bannedTeam, challengeId, 500),
            ScoreEvent(competitionId, activeTeam, challengeId, 250));
        await db.SaveChangesAsync();

        await new CtfScoreRebuilder(db).RebuildCompetitionAsync(competitionId);

        var events = await db.ScoreEvents.IgnoreQueryFilters()
            .Where(e => e.ScoringKey == ScoringKeys.DecaySolve)
            .ToListAsync();
        var rebuilt = Assert.Single(events);
        Assert.Equal(activeTeam, rebuilt.TeamId);
        Assert.Equal(1000, rebuilt.PointsDelta);
    }

    [Fact]
    public async Task RebuildCompetition_RemovesScoresForDeletedChallenges()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        SeedTeam(db, competitionId, teamId, "team");
        db.ScoreEvents.Add(ScoreEvent(competitionId, teamId, Guid.NewGuid(), 500));
        await db.SaveChangesAsync();

        await new CtfScoreRebuilder(db).RebuildCompetitionAsync(competitionId);

        Assert.Empty(await db.ScoreEvents.IgnoreQueryFilters().ToListAsync());
    }

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContextRebuilder(competitionId));
    }

    private static void SeedCompetition(ApplicationDbContext db, Guid competitionId)
        => db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "ctf",
            GameModeType = GameModeType.Ctf,
            ModeKey = "ctf",
            OwnerId = Guid.NewGuid(),
            StartTime = DateTime.UtcNow.AddHours(-1),
            EndTime = DateTime.UtcNow.AddHours(1),
            Status = CompetitionStatus.Running
        });

    private static void SeedChallenge(ApplicationDbContext db, Guid competitionId, Guid challengeId)
        => db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            CompetitionId = competitionId,
            Title = "baby",
            TypeId = "PWN",
            PointsConfig = new PointsConfig(),
            CreatedAt = DateTime.UtcNow
        });

    private static void SeedTeam(ApplicationDbContext db, Guid competitionId, Guid teamId, string name, bool banned = false)
        => db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = name,
            CaptainId = Guid.NewGuid(),
            InviteToken = Guid.NewGuid().ToString("N"),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            IsBanned = banned,
            RegisteredAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });

    private static Submission CorrectSubmission(Guid competitionId, Guid teamId, Guid challengeId, DateTime submittedAt)
        => new()
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            UserId = Guid.NewGuid(),
            FlagContent = "redacted",
            IsCorrect = true,
            SubmittedAt = submittedAt,
            IpAddress = "127.0.0.1"
        };

    private static ScoreEvent ScoreEvent(Guid competitionId, Guid teamId, Guid challengeId, int points)
        => new()
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            ScoringKey = ScoringKeys.DecaySolve,
            EventType = "seed",
            PointsDelta = points,
            Timestamp = DateTime.UtcNow,
            IdempotencyKey = Guid.NewGuid().ToString("N")
        };
}

file sealed class FixedTenantContextRebuilder(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
