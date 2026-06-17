using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Plugins.AWD;

namespace NoCTF.Tests;

/// <summary>
/// Tests for AwdCheckerService: result mapping, throttling, and skip-when-no-checker-config.
/// </summary>
public class AwdCheckerServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContext3(competitionId));
    }

    private static AwdCheckerService CreateService(ApplicationDbContext db, IContainerManager containerManager)
        => new(db, containerManager, NullLogger<AwdCheckerService>.Instance);

    private static void SeedCompetition(ApplicationDbContext db, Guid competitionId)
    {
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "Test AWD",
            OwnerId = Guid.NewGuid(),
            GameModeType = GameModeType.Awd,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(2),
            Status = CompetitionStatus.Running
        });
    }

    private static Guid SeedChallengeWithChecker(ApplicationDbContext db, Guid competitionId, string image = "checker:latest")
    {
        var id = Guid.NewGuid();
        db.Challenges.Add(new Challenge
        {
            Id = id,
            CompetitionId = competitionId,
            Title = "Challenge",
            TypeId = "ctf",
            PointsConfig = new PointsConfig(),
            CheckerConfig = new CheckerConfig { Image = image, Command = "sh -c 'exit 0'", TimeoutSeconds = 5 },
            CreatedAt = DateTime.UtcNow
        });
        return id;
    }

    private static Guid SeedChallengeWithoutChecker(ApplicationDbContext db, Guid competitionId)
    {
        var id = Guid.NewGuid();
        db.Challenges.Add(new Challenge
        {
            Id = id,
            CompetitionId = competitionId,
            Title = "No-checker challenge",
            TypeId = "ctf",
            PointsConfig = new PointsConfig(),
            CreatedAt = DateTime.UtcNow
        });
        return id;
    }

    private static Guid SeedGameBox(ApplicationDbContext db, Guid competitionId, Guid teamId, Guid challengeId)
    {
        var id = Guid.NewGuid();
        db.AwdGameBoxes.Add(new AwdGameBox
        {
            Id = id,
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            CreatedAt = DateTime.UtcNow
        });
        return id;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RunCheckerAsync_ExitCode0_SavesHealthyResult()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId);
        var teamId = Guid.NewGuid();
        var challengeId = SeedChallengeWithChecker(db, competitionId);
        SeedGameBox(db, competitionId, teamId, challengeId);
        await db.SaveChangesAsync();

        var containerManager = new RecordingContainerManager(exitCode: 0);
        var service = CreateService(db, containerManager);

        await service.RunCheckerAsync(competitionId, roundNumber: 1);

        var results = await db.AwdCheckResults.IgnoreQueryFilters().ToListAsync();
        Assert.Single(results);
        Assert.Equal(AwdCheckStatus.Healthy, results[0].Status);
        Assert.Equal(teamId, results[0].TeamId);
        Assert.Equal(challengeId, results[0].ChallengeId);
        Assert.Equal(1, results[0].RoundNumber);
    }

    [Fact]
    public async Task RunCheckerAsync_ExitCode1_SavesDownResult()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId);
        var teamId = Guid.NewGuid();
        var challengeId = SeedChallengeWithChecker(db, competitionId);
        SeedGameBox(db, competitionId, teamId, challengeId);
        await db.SaveChangesAsync();

        var containerManager = new RecordingContainerManager(exitCode: 1);
        var service = CreateService(db, containerManager);

        await service.RunCheckerAsync(competitionId, roundNumber: 2);

        var results = await db.AwdCheckResults.IgnoreQueryFilters().ToListAsync();
        Assert.Single(results);
        Assert.Equal(AwdCheckStatus.Down, results[0].Status);
    }

    [Fact]
    public async Task RunCheckerAsync_NonZeroNonOneExitCode_SavesErrorResult()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId);
        var teamId = Guid.NewGuid();
        var challengeId = SeedChallengeWithChecker(db, competitionId);
        SeedGameBox(db, competitionId, teamId, challengeId);
        await db.SaveChangesAsync();

        var containerManager = new RecordingContainerManager(exitCode: 2);
        var service = CreateService(db, containerManager);

        await service.RunCheckerAsync(competitionId, roundNumber: 1);

        var results = await db.AwdCheckResults.IgnoreQueryFilters().ToListAsync();
        Assert.Single(results);
        Assert.Equal(AwdCheckStatus.Error, results[0].Status);
    }

    [Fact]
    public async Task RunCheckerAsync_ChallengeWithoutCheckerConfig_SkipsAndSavesNoResult()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId);
        var teamId = Guid.NewGuid();
        var challengeId = SeedChallengeWithoutChecker(db, competitionId);
        SeedGameBox(db, competitionId, teamId, challengeId);
        await db.SaveChangesAsync();

        var containerManager = new RecordingContainerManager(exitCode: 0);
        var service = CreateService(db, containerManager);

        await service.RunCheckerAsync(competitionId, roundNumber: 1);

        var results = await db.AwdCheckResults.IgnoreQueryFilters().ToListAsync();
        Assert.Empty(results);
        Assert.Equal(0, containerManager.RunCount);
    }

    [Fact]
    public async Task RunCheckerAsync_MultipleGameBoxes_RunsCheckerForEach()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId);
        var challengeId = SeedChallengeWithChecker(db, competitionId);

        const int teamCount = 6;
        for (int i = 0; i < teamCount; i++)
            SeedGameBox(db, competitionId, Guid.NewGuid(), challengeId);

        await db.SaveChangesAsync();

        var containerManager = new RecordingContainerManager(exitCode: 0);
        var service = CreateService(db, containerManager);

        await service.RunCheckerAsync(competitionId, roundNumber: 1);

        Assert.Equal(teamCount, containerManager.RunCount);

        var results = await db.AwdCheckResults.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(teamCount, results.Count);
        Assert.All(results, r => Assert.Equal(AwdCheckStatus.Healthy, r.Status));
    }

    [Fact]
    public async Task RunCheckerAsync_ThrottlesTo4Concurrent()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId);
        var challengeId = SeedChallengeWithChecker(db, competitionId);

        const int teamCount = 10;
        for (int i = 0; i < teamCount; i++)
            SeedGameBox(db, competitionId, Guid.NewGuid(), challengeId);

        await db.SaveChangesAsync();

        var containerManager = new ThrottleTrackingContainerManager();
        var service = CreateService(db, containerManager);

        await service.RunCheckerAsync(competitionId, roundNumber: 1);

        Assert.Equal(teamCount, containerManager.RunCount);
        // Max observed concurrency must not exceed 4
        Assert.True(containerManager.MaxObservedConcurrency <= 4,
            $"Max concurrency was {containerManager.MaxObservedConcurrency}, expected <= 4");
    }

    [Fact]
    public async Task RunCheckerAsync_ContainerThrows_SavesErrorResult()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId);
        var teamId = Guid.NewGuid();
        var challengeId = SeedChallengeWithChecker(db, competitionId);
        SeedGameBox(db, competitionId, teamId, challengeId);
        await db.SaveChangesAsync();

        var containerManager = new ThrowingContainerManager();
        var service = CreateService(db, containerManager);

        await service.RunCheckerAsync(competitionId, roundNumber: 1);

        var results = await db.AwdCheckResults.IgnoreQueryFilters().ToListAsync();
        Assert.Single(results);
        Assert.Equal(AwdCheckStatus.Error, results[0].Status);
        Assert.NotNull(results[0].Detail);
    }

    [Fact]
    public async Task RunCheckerAsync_PassesCorrectEnvVars()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);

        SeedCompetition(db, competitionId);
        var teamId = Guid.NewGuid();
        var challengeId = SeedChallengeWithChecker(db, competitionId);
        SeedGameBox(db, competitionId, teamId, challengeId);
        await db.SaveChangesAsync();

        var containerManager = new RecordingContainerManager(exitCode: 0);
        var service = CreateService(db, containerManager);

        await service.RunCheckerAsync(competitionId, roundNumber: 5);

        Assert.Single(containerManager.Configs);
        var env = containerManager.Configs[0].EnvironmentVariables!;
        Assert.Equal("5", env["ROUND"]);
        Assert.Equal(teamId.ToString(), env["TEAM_ID"]);
        Assert.Contains("TARGET_HOST", env.Keys);
        Assert.Contains("TARGET_PORT", env.Keys);
    }

    // ── Mock container managers ───────────────────────────────────────────────

    private sealed class RecordingContainerManager(int exitCode) : IContainerManager
    {
        public int RunCount { get; private set; }
        public List<ContainerConfig> Configs { get; } = [];

        public Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken ct = default)
            => Task.FromResult(new ContainerInstance(
                Guid.NewGuid(), Guid.Empty, null, null, "null", "null-id",
                new Dictionary<int, int>(), "running", DateTime.UtcNow));

        public Task DestroyContainerAsync(ContainerInstance container, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken ct = default)
        {
            RunCount++;
            Configs.Add(config);
            return Task.FromResult(new ContainerRunResult(
                "test-id", exitCode, null, null, DateTime.UtcNow, DateTime.UtcNow));
        }

        public Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken ct = default)
            => Task.FromResult(new ComposeDeployment(
                Guid.NewGuid(), Guid.Empty, null, null, "null", config.ProjectName,
                config.ComposeYaml, "running", DateTime.UtcNow));

        public Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class ThrottleTrackingContainerManager : IContainerManager
    {
        private int _current;
        public int RunCount;
        public int MaxObservedConcurrency;

        public Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken ct = default)
            => Task.FromResult(new ContainerInstance(
                Guid.NewGuid(), Guid.Empty, null, null, "null", "null-id",
                new Dictionary<int, int>(), "running", DateTime.UtcNow));

        public Task DestroyContainerAsync(ContainerInstance container, CancellationToken ct = default)
            => Task.CompletedTask;

        public async Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken ct = default)
        {
            var current = Interlocked.Increment(ref _current);
            // Track max concurrency
            int observed;
            do
            {
                observed = MaxObservedConcurrency;
                if (current <= observed) break;
            } while (Interlocked.CompareExchange(ref MaxObservedConcurrency, current, observed) != observed);

            await Task.Delay(10, ct); // simulate work
            Interlocked.Decrement(ref _current);
            Interlocked.Increment(ref RunCount);
            return new ContainerRunResult("test-id", 0, null, null, DateTime.UtcNow, DateTime.UtcNow);
        }

        public Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken ct = default)
            => Task.FromResult(new ComposeDeployment(
                Guid.NewGuid(), Guid.Empty, null, null, "null", config.ProjectName,
                config.ComposeYaml, "running", DateTime.UtcNow));

        public Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class ThrowingContainerManager : IContainerManager
    {
        public Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken ct = default)
            => Task.FromResult(new ContainerInstance(
                Guid.NewGuid(), Guid.Empty, null, null, "null", "null-id",
                new Dictionary<int, int>(), "running", DateTime.UtcNow));

        public Task DestroyContainerAsync(ContainerInstance container, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken ct = default)
            => throw new InvalidOperationException("Docker daemon unavailable.");

        public Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken ct = default)
            => throw new InvalidOperationException("Docker daemon unavailable.");

        public Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}

/// <summary>Fixed tenant context for AwdCheckerService tests.</summary>
file class FixedTenantContext3(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { /* no-op */ }
}
