using Microsoft.EntityFrameworkCore;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Plugins.AWDP;

namespace NoCTF.Tests;

public class AwdpGameModeTests
{
    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContextAwdpGameMode(competitionId));
    }

    private static AwdpGameMode CreateGameMode(
        ApplicationDbContext db,
        ICompetitionExecutionLease? executionLease = null)
        => new(db, new AwdpConfigResolver(db), new AwdpStateService(db), executionLease);

    [Fact]
    public async Task ProcessSubmissionAsync_WithoutInstance_ReturnsInstanceRequiredWithoutCountingAttempt()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        SeedTeam(db, competitionId, teamId);
        var challengeId = SeedChallenge(db, competitionId);
        await db.SaveChangesAsync();

        var gameMode = CreateGameMode(db);
        var result = await gameMode.ProcessSubmissionAsync(CreateSubmissionContext(competitionId, teamId, challengeId));

        Assert.Equal(SubmissionResult.InstanceRequired, result);

        var state = await db.AwdpTeamChallengeStates.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(0, state.AttackAttempts);
        Assert.Equal(AwdpInstanceStatus.InstanceNotCreated, state.InstanceStatus);
    }

    [Fact]
    public async Task ProcessSubmissionAsync_RunningInstance_RecordsBreakSuccessWithoutImmediateScore()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        SeedTeam(db, competitionId, teamId);
        var challengeId = SeedChallenge(db, competitionId);
        SeedGameBox(db, competitionId, teamId, challengeId);
        await db.SaveChangesAsync();

        var gameMode = CreateGameMode(db);
        var result = await gameMode.ProcessSubmissionAsync(CreateSubmissionContext(competitionId, teamId, challengeId));

        Assert.Equal(SubmissionResult.Accepted, result);

        var state = await db.AwdpTeamChallengeStates.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(1, state.AttackAttempts);
        Assert.Equal(AwdpBreakStatus.BreakSuccess, state.BreakStatus);
        Assert.Equal(AwdpInstanceStatus.InstanceRunning, state.InstanceStatus);
        Assert.Empty(await db.ScoreEvents.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task ProcessSubmissionAsync_AttackAttemptsExhausted_DoesNotContinueSubmitting()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        SeedTeam(db, competitionId, teamId);
        var challengeId = SeedChallenge(db, competitionId, maxAttackAttempts: 1);
        SeedGameBox(db, competitionId, teamId, challengeId);
        db.AwdpTeamChallengeStates.Add(new AwdpTeamChallengeState
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            AttackAttempts = 1,
            BreakStatus = AwdpBreakStatus.AttackAttemptsExhausted,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var gameMode = CreateGameMode(db);
        var result = await gameMode.ProcessSubmissionAsync(CreateSubmissionContext(competitionId, teamId, challengeId));

        Assert.Equal(SubmissionResult.AttemptsExhausted, result);

        var state = await db.AwdpTeamChallengeStates.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(1, state.AttackAttempts);
        Assert.Equal(AwdpBreakStatus.AttackAttemptsExhausted, state.BreakStatus);
        Assert.Empty(await db.Submissions.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task ProcessSubmissionAsync_ChallengeTombstonedAfterPreRead_DoesNotRecreateBreakState()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        SeedTeam(db, competitionId, teamId);
        var challengeId = SeedChallenge(db, competitionId);
        SeedGameBox(db, competitionId, teamId, challengeId);
        await db.SaveChangesAsync();
        var lease = new MutatingExecutionLease(async (leaseDb, ct) =>
        {
            var challenge = await leaseDb.Challenges.IgnoreQueryFilters().SingleAsync(c => c.Id == challengeId, ct);
            challenge.IsDeleting = true;
            await leaseDb.SaveChangesAsync(ct);
        });

        var result = await CreateGameMode(db, lease).ProcessSubmissionAsync(
            CreateSubmissionContext(competitionId, teamId, challengeId));

        Assert.Equal(SubmissionResult.WrongFlag, result);
        Assert.Empty(await db.Submissions.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.AwdpTeamChallengeStates.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.CompetitionLogs.IgnoreQueryFilters().ToListAsync());
    }

    private static SubmissionContext CreateSubmissionContext(
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        string flag = "flag{secret}")
        => new(
            CompetitionId: competitionId,
            TeamId: teamId,
            ChallengeId: challengeId,
            UserId: Guid.NewGuid(),
            FlagContent: flag,
            IpAddress: "127.0.0.1");

    private static void SeedCompetition(
        ApplicationDbContext db,
        Guid competitionId)
    {
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "AWDP Test",
            OwnerId = Guid.NewGuid(),
            GameModeType = GameModeType.Awdp,
            ModeKey = "awdp",
            StartTime = DateTime.UtcNow.AddMinutes(-1),
            EndTime = DateTime.UtcNow.AddHours(2),
            Status = CompetitionStatus.Running
        });
    }

    private static Guid SeedChallenge(ApplicationDbContext db, Guid competitionId, int maxAttackAttempts = 5)
    {
        var id = Guid.NewGuid();
        db.Challenges.Add(new Challenge
        {
            Id = id,
            CompetitionId = competitionId,
            Title = "Vuln Service",
            TypeId = "awdp",
            FlagSecret = "secret",
            PointsConfig = new PointsConfig(),
            ContainerImage = "vuln-service:latest",
            ExposedPort = 80,
            AwdpMaxAttackAttempts = maxAttackAttempts,
            AwdpFixEntry = "fix.sh",
            CreatedAt = DateTime.UtcNow
        });
        return id;
    }

    private static void SeedTeam(ApplicationDbContext db, Guid competitionId, Guid teamId)
    {
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "AWDP Team",
            CaptainId = Guid.NewGuid(),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static void SeedGameBox(
        ApplicationDbContext db,
        Guid competitionId,
        Guid teamId,
        Guid challengeId)
    {
        db.AwdGameBoxes.Add(new AwdGameBox
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            ContainerInstanceId = "container-123",
            PortMappingsJson = "{}",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow
        });
    }

    private sealed class MutatingExecutionLease(
        Func<ApplicationDbContext, CancellationToken, Task> mutation) : ICompetitionExecutionLease
    {
        public async Task<IExecutionLease?> TryAcquireAsync(
            ApplicationDbContext db,
            string engineKey,
            Guid competitionId,
            CancellationToken ct = default)
        {
            Assert.Equal(CompetitionExecutionLeaseKeys.RuntimePreparation, engineKey);
            await mutation(db, ct);
            return new NoopExecutionLease();
        }
    }

    private sealed class NoopExecutionLease : IExecutionLease
    {
        public CancellationToken LostToken => CancellationToken.None;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

file class FixedTenantContextAwdpGameMode(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
