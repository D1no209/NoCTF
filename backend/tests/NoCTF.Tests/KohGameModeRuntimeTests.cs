using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Plugins.KoH;

namespace NoCTF.Tests;

public class KohGameModeRuntimeTests
{
    [Fact]
    public async Task InitializeAsync_FailedCreatePersistsSkeletonAndReusesOperationOnRetry()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var endTime = DateTime.UtcNow.AddHours(2);
        await using var db = CreateDb(competitionId);
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "KoH runtime",
            OwnerId = Guid.NewGuid(),
            GameModeType = GameModeType.Koh,
            ModeKey = "koh",
            StartTime = DateTime.UtcNow.AddMinutes(-1),
            EndTime = endTime,
            Status = CompetitionStatus.Running
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            CompetitionId = competitionId,
            Title = "KoH hill",
            TypeId = "koh",
            PointsConfig = new PointsConfig(),
            ContainerImage = "hill:latest",
            ExposedPort = 8080,
            KohAgentConfig = new KohAgentConfig { Port = 8080 },
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var manager = new RetryContainerManager();
        var mode = new KohGameMode(db, manager, NullLogger<KohGameMode>.Instance);
        var context = new GameContext(
            competitionId,
            GameModeType.Koh,
            DateTime.UtcNow.AddMinutes(-1),
            endTime,
            new Dictionary<string, string>());

        await mode.InitializeAsync(context);

        var skeleton = await db.AwdGameBoxes.IgnoreQueryFilters().SingleAsync();
        Assert.Null(skeleton.ContainerInstanceId);
        Assert.NotNull(skeleton.RuntimeOperationId);
        Assert.Equal(endTime, skeleton.ExpiresAt);
        var operationId = skeleton.RuntimeOperationId;

        await mode.InitializeAsync(context);

        var recovered = await db.AwdGameBoxes.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("hill-container", recovered.ContainerInstanceId);
        Assert.Equal("runner.test", recovered.InternalHost);
        Assert.Equal(operationId, recovered.RuntimeOperationId);
        Assert.Equal(2, manager.Configs.Count);
        Assert.All(manager.Configs, config => Assert.Equal(operationId, config.OperationId));
        Assert.All(manager.Configs, config => Assert.True(config.Ttl > TimeSpan.FromHours(1)));
        Assert.NotNull(recovered.ExpiresAt);
    }

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new KohRuntimeTenantContext(competitionId));
    }

    private sealed class RetryContainerManager : IContainerManager
    {
        public List<ContainerConfig> Configs { get; } = [];

        public Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken ct = default)
        {
            Configs.Add(config);
            if (Configs.Count == 1)
                throw new InvalidOperationException("Runner unavailable after request dispatch.");

            return Task.FromResult(new ContainerInstance(
                Guid.NewGuid(),
                Guid.Empty,
                null,
                null,
                "docker",
                "hill-container",
                new Dictionary<int, int> { [8080] = 32080 },
                "running",
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(1),
                InternalHost: "runner.test",
                InternalPortMappings: new Dictionary<int, int> { [8080] = 32080 }));
        }

        public Task DestroyContainerAsync(ContainerInstance container, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}

file sealed class KohRuntimeTenantContext(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
