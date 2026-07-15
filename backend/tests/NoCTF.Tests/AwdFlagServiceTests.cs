using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.Plugins.AWD;
using NoCTF.PluginBase;

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
            .UseSharedInMemoryServiceProvider()
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

    [Fact]
    public async Task RefreshFlagsAsync_UsesStableNetworkAliasAndCompetitionTtl()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var flagId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        SeedTeam(db, competitionId, teamId);
        SeedChallenge(db, competitionId, challengeId, "ctf", "secret");
        await db.SaveChangesAsync();
        var challenge = await db.Challenges.IgnoreQueryFilters().SingleAsync(c => c.Id == challengeId);
        challenge.ContainerImage = "challenge:latest";
        challenge.ExposedPort = 8080;
        db.AwdFlags.Add(new AwdFlag
        {
            Id = flagId,
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            RoundNumber = 1,
            FlagContent = "flag{round-one}",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var manager = new RefreshContainerManager();
        var service = new AwdFlagService(
            db,
            manager,
            new AwdChallengeRuntimeConfigProvider(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AwdFlagService>.Instance);
        await service.RefreshFlagsAsync(competitionId, 1);

        var config = Assert.Single(manager.CreateConfigs);
        Assert.Equal(flagId, config.OperationId);
        Assert.Contains(
            $"gamebox-{teamId:N}"[..16] + $"-{challengeId:N}"[..9],
            config.NetworkAliases!);
        Assert.True(config.Ttl > TimeSpan.FromHours(1));
        var box = await db.AwdGameBoxes.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(flagId, box.RuntimeOperationId);
        Assert.NotNull(box.ExpiresAt);
        Assert.Equal("created-container", box.ContainerInstanceId);
    }

    [Fact]
    public async Task RefreshFlagsAsync_CreateFailureAfterDestroy_PersistsTruthfulRetryState()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var flagId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        SeedTeam(db, competitionId, teamId);
        SeedChallenge(db, competitionId, challengeId, "ctf", "secret");
        await db.SaveChangesAsync();
        var challenge = await db.Challenges.IgnoreQueryFilters().SingleAsync(c => c.Id == challengeId);
        challenge.ContainerImage = "challenge:latest";
        challenge.ExposedPort = 8080;
        db.AwdFlags.Add(new AwdFlag
        {
            Id = flagId,
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            RoundNumber = 1,
            FlagContent = "flag{round-one}",
            CreatedAt = DateTime.UtcNow
        });
        db.AwdGameBoxes.Add(new AwdGameBox
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            ContainerInstanceId = "destroyed-container",
            PublicHost = "stale.example.test",
            EntryUrl = "http://stale.example.test",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var manager = new RefreshContainerManager(failCreate: true);
        var service = new AwdFlagService(
            db,
            manager,
            new AwdChallengeRuntimeConfigProvider(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AwdFlagService>.Instance);
        var completed = await service.RefreshRoundFlagsAsync(competitionId, 1);

        var box = await db.AwdGameBoxes.IgnoreQueryFilters().SingleAsync();
        Assert.False(completed);
        Assert.Equal("destroyed-container", Assert.Single(manager.DestroyedContainerIds));
        Assert.Null(box.ContainerInstanceId);
        Assert.Null(box.PublicHost);
        Assert.Null(box.EntryUrl);
        Assert.Null(box.ExpiresAt);
        Assert.Equal(flagId, box.RuntimeOperationId);
        Assert.NotNull(box.LastInstanceActionAt);
    }

    [Fact]
    public async Task RefreshFlagsAsync_RepeatedRound_DoesNotDestroySuccessfulOperation()
    {
        var competitionId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var flagId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        SeedTeam(db, competitionId, teamId);
        SeedChallenge(db, competitionId, challengeId, "ctf", "secret");
        await db.SaveChangesAsync();
        var challenge = await db.Challenges.IgnoreQueryFilters().SingleAsync(c => c.Id == challengeId);
        challenge.ContainerImage = "challenge:latest";
        challenge.ExposedPort = 8080;
        db.AwdFlags.Add(new AwdFlag
        {
            Id = flagId,
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            RoundNumber = 1,
            FlagContent = "flag{round-one}",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var manager = new RefreshContainerManager();
        var service = new AwdFlagService(
            db,
            manager,
            new AwdChallengeRuntimeConfigProvider(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AwdFlagService>.Instance);
        await service.RefreshFlagsAsync(competitionId, 1);
        await service.RefreshFlagsAsync(competitionId, 1);

        Assert.Single(manager.CreateConfigs);
        Assert.Empty(manager.DestroyedContainerIds);
        Assert.Equal("created-container", (await db.AwdGameBoxes.IgnoreQueryFilters().SingleAsync()).ContainerInstanceId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AwdFlagService CreateService(ApplicationDbContext db)
    {
        // For unit tests we don't need IContainerManager (RefreshFlagsAsync not tested here)
        var containerManager = new NullContainerManager();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AwdFlagService>.Instance;
        return new AwdFlagService(db, containerManager, new AwdChallengeRuntimeConfigProvider(), logger);
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

    private sealed class RefreshContainerManager(bool failCreate = false) : IContainerManager
    {
        public List<ContainerConfig> CreateConfigs { get; } = [];
        public List<string> DestroyedContainerIds { get; } = [];

        public Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken ct = default)
        {
            CreateConfigs.Add(config);
            if (failCreate)
                throw new InvalidOperationException("Create failed.");

            return Task.FromResult(new ContainerInstance(
                Guid.NewGuid(),
                Guid.Empty,
                null,
                null,
                "docker",
                "created-container",
                new Dictionary<int, int> { [8080] = 32000 },
                "running",
                DateTime.UtcNow,
                InternalHost: "runner.test",
                InternalPortMappings: new Dictionary<int, int> { [8080] = 32000 }));
        }

        public Task DestroyContainerAsync(ContainerInstance container, CancellationToken ct = default)
        {
            DestroyedContainerIds.Add(container.ContainerId);
            return Task.CompletedTask;
        }

        public Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}

/// <summary>Fixed tenant context for AwdFlagService tests.</summary>
file class FixedTenantContext2(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { /* no-op */ }
}
