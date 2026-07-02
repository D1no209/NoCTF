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

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
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
}

file class FixedTenantContext(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
