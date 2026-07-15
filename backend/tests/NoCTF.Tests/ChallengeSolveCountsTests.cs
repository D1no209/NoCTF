using Microsoft.EntityFrameworkCore;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Tests;

public class ChallengeSolveCountsTests
{
    [Fact]
    public async Task GetAsync_CountsOnlyApprovedUnbannedTeamsOnce()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var activeTeam = Guid.NewGuid();
        var bannedTeam = Guid.NewGuid();
        var pendingTeam = Guid.NewGuid();

        await using var db = CreateDb(competitionId);
        SeedTeam(db, competitionId, activeTeam, "Active");
        SeedTeam(db, competitionId, bannedTeam, "Banned", isBanned: true);
        SeedTeam(db, competitionId, pendingTeam, "Pending", status: TeamRegistrationStatus.Pending);

        SeedCorrectSubmission(db, competitionId, activeTeam, challengeId);
        SeedCorrectSubmission(db, competitionId, activeTeam, challengeId);
        SeedCorrectSubmission(db, competitionId, bannedTeam, challengeId);
        SeedCorrectSubmission(db, competitionId, pendingTeam, challengeId);
        SeedWrongSubmission(db, competitionId, Guid.NewGuid(), challengeId);
        await db.SaveChangesAsync();

        var result = await ChallengeSolveCounts.GetAsync(db, competitionId, [challengeId]);

        Assert.Equal(1, result[challengeId]);
    }

    [Fact]
    public async Task PenetrationQueries_IgnoreHiddenFlagsForProgressAndPlayerHistory()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var visibleFlagOne = Guid.NewGuid();
        var visibleFlagTwo = Guid.NewGuid();
        var hiddenFlag = Guid.NewGuid();

        await using var db = CreateDb(competitionId);
        SeedTeam(db, competitionId, teamId, "Active");
        db.PenetrationFlags.AddRange(
            Flag(visibleFlagOne, competitionId, challengeId, stage: 1, visible: true),
            Flag(visibleFlagTwo, competitionId, challengeId, stage: 2, visible: true),
            Flag(hiddenFlag, competitionId, challengeId, stage: 99, visible: false));
        SeedPenetrationSubmission(db, competitionId, teamId, challengeId, visibleFlagOne);
        SeedPenetrationSubmission(db, competitionId, teamId, challengeId, hiddenFlag);
        await db.SaveChangesAsync();

        var solvedFlags = await GetSubmissionsEndpoint.BuildVisibleSolvedFlagsQuery(
                db,
                competitionId,
                teamId,
                [challengeId])
            .ToListAsync();
        var incompleteCounts = await ChallengeSolveCounts.GetPenetrationFullSolveCountsAsync(
            db,
            competitionId,
            [challengeId]);

        Assert.Single(solvedFlags);
        Assert.Equal(visibleFlagOne, solvedFlags[0].FlagId);
        Assert.DoesNotContain(challengeId, incompleteCounts.Keys);

        SeedPenetrationSubmission(db, competitionId, teamId, challengeId, visibleFlagTwo);
        await db.SaveChangesAsync();

        var completeCounts = await ChallengeSolveCounts.GetPenetrationFullSolveCountsAsync(
            db,
            competitionId,
            [challengeId]);
        Assert.Equal(1, completeCounts[challengeId]);
    }

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options, new FixedTenantContext(competitionId));
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

    private static void SeedCorrectSubmission(ApplicationDbContext db, Guid competitionId, Guid teamId, Guid challengeId)
        => SeedSubmission(db, competitionId, teamId, challengeId, true);

    private static void SeedWrongSubmission(ApplicationDbContext db, Guid competitionId, Guid teamId, Guid challengeId)
        => SeedSubmission(db, competitionId, teamId, challengeId, false);

    private static void SeedSubmission(
        ApplicationDbContext db,
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        bool isCorrect)
    {
        db.Submissions.Add(new Submission
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            UserId = Guid.NewGuid(),
            FlagContent = isCorrect ? "flag{ok}" : "flag{wrong}",
            IsCorrect = isCorrect,
            SubmittedAt = DateTime.UtcNow,
            IpAddress = "127.0.0.1"
        });
    }

    private static PenetrationFlag Flag(
        Guid id,
        Guid competitionId,
        Guid challengeId,
        int stage,
        bool visible)
        => new()
        {
            Id = id,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            TopologyId = Guid.NewGuid(),
            Name = $"flag-{stage}",
            Stage = stage,
            Score = 100,
            Visible = visible,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private static void SeedPenetrationSubmission(
        ApplicationDbContext db,
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        Guid flagId)
    {
        db.Submissions.Add(new Submission
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            UserId = Guid.NewGuid(),
            PenetrationFlagId = flagId,
            FlagContent = "[REDACTED]",
            IsCorrect = true,
            SubmittedAt = DateTime.UtcNow,
            IpAddress = "127.0.0.1"
        });
    }
}

file class FixedTenantContext(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
