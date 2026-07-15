using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Plugins.Penetration;

namespace NoCTF.Tests;

public class PenetrationScoringTests
{
    [Fact]
    public async Task CtfRebuild_DelegatesPenetrationChallengesAndRestoresStageBloodScores()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var flagId = Guid.NewGuid();
        var firstTeam = Guid.NewGuid();
        var bannedTeam = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "CTF",
            OwnerId = Guid.NewGuid(),
            GameModeType = GameModeType.Ctf,
            ModeKey = "ctf",
            Status = CompetitionStatus.Running,
            StartTime = DateTime.UtcNow.AddHours(-1),
            EndTime = DateTime.UtcNow.AddHours(1),
            FirstBloodBonusPercent = 20
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            CompetitionId = competitionId,
            Title = "Range",
            TypeId = PenetrationConstants.TypeId,
            EnableBloodBonus = true,
            CreatedAt = DateTime.UtcNow
        });
        db.PenetrationFlags.Add(Flag(competitionId, challengeId, flagId, 200));
        SeedTeam(db, competitionId, firstTeam, "First");
        SeedTeam(db, competitionId, bannedTeam, "Banned", isBanned: true);
        SeedSubmission(db, competitionId, firstTeam, challengeId, flagId, DateTime.UtcNow.AddMinutes(-2));
        SeedSubmission(db, competitionId, bannedTeam, challengeId, flagId, DateTime.UtcNow.AddMinutes(-1));
        await db.SaveChangesAsync();

        var rebuilder = new CtfScoreRebuilder(
            db,
            customSubmissionHandlers: null,
            contributors: [new PenetrationScoreRebuildContributor(db)]);
        await rebuilder.RebuildCompetitionAsync(competitionId);

        var events = await db.ScoreEvents.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.DoesNotContain(events, e => e.TeamId == bannedTeam);
        Assert.Contains(events, e => e.ScoringKey == ScoringKeys.PenetrationStage && e.PointsDelta == 200);
        Assert.Contains(events, e => e.ScoringKey == ScoringKeys.PenetrationBloodBonus && e.PointsDelta == 40);
        Assert.DoesNotContain(events, e => e.ScoringKey == ScoringKeys.DecaySolve);
    }

    [Fact]
    public async Task StageStrategy_WritesOneScoreEventForStageSolve()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var flagId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        db.PenetrationFlags.Add(Flag(competitionId, challengeId, flagId, score: 150));
        await db.SaveChangesAsync();

        var writer = new ScoreEventWriter(db);
        var strategy = new PenetrationStageScoringStrategy(db, writer);
        var signal = Signal(competitionId, challengeId, teamId, flagId);

        await strategy.HandleAsync(signal);
        await strategy.HandleAsync(signal);

        var scoreEvents = await db.ScoreEvents.IgnoreQueryFilters().ToListAsync();
        Assert.Single(scoreEvents);
        Assert.Equal(ScoringKeys.PenetrationStage, scoreEvents[0].ScoringKey);
        Assert.Equal(150, scoreEvents[0].PointsDelta);
        Assert.Equal(challengeId, scoreEvents[0].ChallengeId);
    }

    [Fact]
    public async Task BloodBonusStrategy_UsesPerStageScoreAndApprovedTeamsOnly()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var flagId = Guid.NewGuid();
        var firstTeam = Guid.NewGuid();
        var secondTeam = Guid.NewGuid();
        var bannedTeam = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "CTF",
            OwnerId = Guid.NewGuid(),
            GameModeType = GameModeType.Ctf,
            ModeKey = "ctf",
            Status = CompetitionStatus.Running,
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = DateTime.UtcNow.AddHours(2),
            FirstBloodBonusPercent = 20,
            SecondBloodBonusPercent = 10,
            ThirdBloodBonusPercent = 5
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            CompetitionId = competitionId,
            Title = "Range",
            TypeId = PenetrationConstants.TypeId,
            EnableBloodBonus = true,
            CreatedAt = DateTime.UtcNow
        });
        db.PenetrationFlags.Add(Flag(competitionId, challengeId, flagId, score: 200));
        SeedTeam(db, competitionId, firstTeam, "First");
        SeedTeam(db, competitionId, secondTeam, "Second");
        SeedTeam(db, competitionId, bannedTeam, "Banned", isBanned: true);
        SeedSubmission(db, competitionId, firstTeam, challengeId, flagId, DateTime.UtcNow.AddMinutes(-3));
        SeedSubmission(db, competitionId, bannedTeam, challengeId, flagId, DateTime.UtcNow.AddMinutes(-2));
        SeedSubmission(db, competitionId, secondTeam, challengeId, flagId, DateTime.UtcNow.AddMinutes(-1));
        await db.SaveChangesAsync();

        var writer = new ScoreEventWriter(db);
        var strategy = new PenetrationBloodBonusStrategy(db, writer);
        await strategy.HandleAsync(Signal(competitionId, challengeId, firstTeam, flagId));

        var events = await db.ScoreEvents.IgnoreQueryFilters()
            .OrderBy(e => e.TeamId)
            .ToListAsync();

        Assert.Equal(2, events.Count);
        Assert.DoesNotContain(events, e => e.TeamId == bannedTeam);
        Assert.Contains(events, e => e.TeamId == firstTeam && e.PointsDelta == 40);
        Assert.Contains(events, e => e.TeamId == secondTeam && e.PointsDelta == 20);
    }

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider(ignoreTransactionWarnings: true)
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options, new PenetrationScoringTenantContext(competitionId));
    }

    private static PenetrationFlag Flag(Guid competitionId, Guid challengeId, Guid flagId, int score) => new()
    {
        Id = flagId,
        CompetitionId = competitionId,
        ChallengeId = challengeId,
        TopologyId = Guid.NewGuid(),
        Stage = 1,
        Name = "Initial",
        Score = score,
        IsDynamic = true,
        InjectionKey = "NOCTF_STAGE1",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static ScoreSignal Signal(Guid competitionId, Guid challengeId, Guid teamId, Guid flagId) => new()
    {
        Id = Guid.NewGuid(),
        CompetitionId = competitionId,
        TeamId = teamId,
        SubjectType = "penetration-flag",
        SubjectId = flagId,
        SignalType = ScoreSignalTypes.PenetrationFlagAccepted,
        OccurredAt = DateTime.UtcNow,
        IdempotencyKey = $"penetration:{teamId:N}:{challengeId:N}:{flagId:N}"
    };

    private static void SeedTeam(ApplicationDbContext db, Guid competitionId, Guid teamId, string name, bool isBanned = false)
        => db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = name,
            CaptainId = Guid.NewGuid(),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            IsBanned = isBanned,
            CreatedAt = DateTime.UtcNow
        });

    private static void SeedSubmission(
        ApplicationDbContext db,
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        Guid flagId,
        DateTime submittedAt)
        => db.Submissions.Add(new Submission
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            PenetrationFlagId = flagId,
            UserId = Guid.NewGuid(),
            FlagContent = "sha256:test;len:10",
            IsCorrect = true,
            SubmittedAt = submittedAt,
            IpAddress = "127.0.0.1"
        });

    private sealed class PenetrationHandlerStub : IChallengeSubmissionHandler
    {
        public string TypeId => PenetrationConstants.TypeId;

        public Task<ChallengeSubmissionResult> ProcessSubmissionAsync(
            SubmissionContext context,
            Challenge challenge,
            CancellationToken ct = default)
            => Task.FromResult(new ChallengeSubmissionResult(SubmissionResult.WrongFlag));
    }
}

file class PenetrationScoringTenantContext(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
