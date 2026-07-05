using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.Plugins.AWD;

namespace NoCTF.Tests;

/// <summary>
/// Tests for AwdFlagService: deterministic generation, uniqueness, and DB constraint.
/// </summary>
public class AwdFlagServiceTests
{
    // ── Static ComputeFlag tests (no DB needed) ───────────────────────────────

    [Fact]
    public void ComputeFlag_SameInput_ReturnsSameFlag()
    {
        var teamId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var challengeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        const int round = 3;
        const string secret = "test-secret";

        var flag1 = AwdFlagService.ComputeFlag(teamId, challengeId, round, secret);
        var flag2 = AwdFlagService.ComputeFlag(teamId, challengeId, round, secret);

        Assert.Equal(flag1, flag2);
    }

    [Fact]
    public void ComputeFlag_DifferentRound_ReturnsDifferentFlag()
    {
        var teamId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var challengeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        const string secret = "test-secret";

        var flag1 = AwdFlagService.ComputeFlag(teamId, challengeId, 1, secret);
        var flag2 = AwdFlagService.ComputeFlag(teamId, challengeId, 2, secret);

        Assert.NotEqual(flag1, flag2);
    }

    [Fact]
    public void ComputeFlag_DifferentTeam_ReturnsDifferentFlag()
    {
        var teamA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var teamB = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var challengeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        const string secret = "test-secret";

        var flagA = AwdFlagService.ComputeFlag(teamA, challengeId, 1, secret);
        var flagB = AwdFlagService.ComputeFlag(teamB, challengeId, 1, secret);

        Assert.NotEqual(flagA, flagB);
    }

    [Fact]
    public void ComputeFlag_DifferentChallenge_ReturnsDifferentFlag()
    {
        var teamId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var challengeA = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var challengeB = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        const string secret = "test-secret";

        var flagA = AwdFlagService.ComputeFlag(teamId, challengeA, 1, secret);
        var flagB = AwdFlagService.ComputeFlag(teamId, challengeB, 1, secret);

        Assert.NotEqual(flagA, flagB);
    }

    [Fact]
    public void ComputeFlag_NullSecret_Throws()
    {
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() =>
            AwdFlagService.ComputeFlag(teamId, challengeId, 1, null));
    }

    // ── DB-level GenerateFlagsAsync tests ─────────────────────────────────────

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContext2(competitionId));
    }

    [Fact]
    public async Task GenerateFlagsAsync_CreatesExpectedFlagCount()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        SeedCompetition(db, competitionId);
        SeedTeam(db, competitionId, teamId);
        SeedChallenge(db, competitionId, challengeId, "ctf", "secret");
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.GenerateFlagsAsync(competitionId, totalRounds: 3);

        var flags = await db.AwdFlags.IgnoreQueryFilters().ToListAsync();
        // 1 team × 1 challenge × 3 rounds = 3 flags
        Assert.Equal(3, flags.Count);
    }

    [Fact]
    public async Task GenerateFlagsAsync_IsIdempotent()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId);
        SeedTeam(db, competitionId, Guid.NewGuid());
        SeedChallenge(db, competitionId, Guid.NewGuid(), "ctf", "secret");
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.GenerateFlagsAsync(competitionId, totalRounds: 2);
        await service.GenerateFlagsAsync(competitionId, totalRounds: 2); // second call

        var flags = await db.AwdFlags.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, flags.Count); // no duplicates
    }

    [Fact]
    public async Task GenerateFlagsAsync_FlagsAreWrappedInFlagFormat()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId);
        SeedTeam(db, competitionId, Guid.NewGuid());
        SeedChallenge(db, competitionId, Guid.NewGuid(), "ctf", "secret");
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.GenerateFlagsAsync(competitionId, totalRounds: 1);

        var flag = await db.AwdFlags.IgnoreQueryFilters().FirstAsync();
        Assert.StartsWith("flag{", flag.FlagContent);
        Assert.EndsWith("}", flag.FlagContent);
    }

    [Fact]
    public async Task GenerateFlagsAsync_UniqueConstraint_DifferentRoundsDifferentFlags()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        SeedCompetition(db, competitionId);
        SeedTeam(db, competitionId, teamId);
        SeedChallenge(db, competitionId, challengeId, "ctf", "secret");
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.GenerateFlagsAsync(competitionId, totalRounds: 5);

        var flags = await db.AwdFlags.IgnoreQueryFilters()
            .Where(f => f.TeamId == teamId && f.ChallengeId == challengeId)
            .Select(f => f.FlagContent)
            .ToListAsync();

        // All 5 round flags must be distinct
        Assert.Equal(5, flags.Distinct().Count());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AwdFlagService CreateService(ApplicationDbContext db)
    {
        // For unit tests we don't need IContainerManager (RefreshFlagsAsync not tested here)
        var containerManager = new NullContainerManager();
        var challengeTypes = Enumerable.Empty<NoCTF.PluginBase.IChallengeType>();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AwdFlagService>.Instance;
        return new AwdFlagService(db, containerManager, challengeTypes, logger);
    }

    private static void SeedCompetition(ApplicationDbContext db, Guid competitionId)
    {
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "Test AWD",
            OwnerId = Guid.NewGuid(),
            GameModeType = NoCTF.PluginBase.GameModeType.Awd,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(2),
            Status = NoCTF.Core.CompetitionStatus.Running
        });
    }

    private static void SeedTeam(ApplicationDbContext db, Guid competitionId, Guid teamId)
    {
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "Team " + teamId.ToString("N")[..6],
            CaptainId = Guid.NewGuid(),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static void SeedChallenge(ApplicationDbContext db, Guid competitionId, Guid challengeId, string typeId, string secret)
    {
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            CompetitionId = competitionId,
            Title = "Challenge " + challengeId.ToString("N")[..6],
            TypeId = typeId,
            FlagSecret = secret,
            PointsConfig = new PointsConfig(),
            CreatedAt = DateTime.UtcNow
        });
    }

    /// <summary>No-op container manager for unit tests.</summary>
    private sealed class NullContainerManager : NoCTF.PluginBase.IContainerManager
    {
        public Task<NoCTF.PluginBase.ContainerInstance> CreateContainerAsync(
            NoCTF.PluginBase.ContainerConfig config, CancellationToken cancellationToken = default)
            => Task.FromResult(new NoCTF.PluginBase.ContainerInstance(
                Guid.NewGuid(), Guid.Empty, null, null, "null", "null-container-id",
                new Dictionary<int, int>(), "running", DateTime.UtcNow));

        public Task DestroyContainerAsync(
            NoCTF.PluginBase.ContainerInstance container, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<NoCTF.PluginBase.ContainerRunResult> RunContainerAsync(
            NoCTF.PluginBase.ContainerConfig config, CancellationToken cancellationToken = default)
            => Task.FromResult(new NoCTF.PluginBase.ContainerRunResult(
                "null-container-id", 0, null, null, DateTime.UtcNow, DateTime.UtcNow));

        public Task<NoCTF.PluginBase.ComposeDeployment> ComposeUpAsync(
            NoCTF.PluginBase.ComposeConfig config, CancellationToken cancellationToken = default)
            => Task.FromResult(new NoCTF.PluginBase.ComposeDeployment(
                Guid.NewGuid(), Guid.Empty, null, null, "null", config.ProjectName,
                config.ComposeYaml, "running", DateTime.UtcNow));

        public Task ComposeDownAsync(
            NoCTF.PluginBase.ComposeDeployment deployment, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

/// <summary>Fixed tenant context for AwdFlagService tests.</summary>
file class FixedTenantContext2(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { /* no-op */ }
}
