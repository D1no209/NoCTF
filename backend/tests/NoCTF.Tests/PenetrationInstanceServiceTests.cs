using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Plugins.Penetration;

namespace NoCTF.Tests;

public class PenetrationInstanceServiceTests
{
    [Fact]
    public async Task ResetAsync_RestartsImmediatelyAfterResetAndRegeneratesDynamicFlags()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var topologyId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var flagId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedCompetition(db, competitionId);
        SeedChallenge(db, competitionId, challengeId);
        db.PenetrationTopologies.Add(new PenetrationTopology
        {
            Id = topologyId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            Name = "Range",
            EntryConfigJson = "{\"scheme\":\"http\"}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.PenetrationNodes.Add(new PenetrationNode
        {
            Id = nodeId,
            CompetitionId = competitionId,
            TopologyId = topologyId,
            Name = "web",
            Image = "nginx:alpine",
            PortsJson = "[80]",
            IsEntry = true,
            IsInternal = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.PenetrationFlags.Add(new PenetrationFlag
        {
            Id = flagId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            TopologyId = topologyId,
            NodeId = nodeId,
            Stage = 1,
            Name = "Initial",
            Score = 100,
            IsDynamic = true,
            InjectionKey = "NOCTF_STAGE1",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.TeamChallengeInstances.Add(new TeamChallengeInstance
        {
            Id = instanceId,
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            TopologyId = topologyId,
            Status = PenetrationInstanceStatus.Running,
            ComposeProjectName = "old-project",
            RenderedComposeYaml = "services:\n  web:\n    image: nginx:alpine\n",
            LastActionAt = DateTime.UtcNow.AddMinutes(-10),
            CreatedAt = DateTime.UtcNow.AddMinutes(-15),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
        });
        db.DynamicFlagInstances.Add(new DynamicFlagInstance
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            TeamId = teamId,
            FlagId = flagId,
            InstanceId = instanceId,
            ValueSecret = "old",
            ValueHash = PenetrationFlagService.Hash("old"),
            IsActive = true,
            GeneratedAt = DateTime.UtcNow.AddMinutes(-10)
        });
        await db.SaveChangesAsync();

        var fakeManager = new FakeComposeContainerManager(nodeId);
        var service = new PenetrationInstanceService(
            db,
            fakeManager,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["InstanceAccess:PublicHost"] = "ctf.local"
                })
                .Build(),
            new PenetrationComposeBuilder(),
            new PenetrationFlagService(db));

        var dto = await service.ResetAsync(competitionId, challengeId, teamId, userId, adminOverride: false, CancellationToken.None);

        Assert.Equal("Running", dto.Status);
        Assert.Equal(1, dto.ResetCount);
        Assert.Equal("http://ctf.local:32080", dto.EntryUrl);
        Assert.True(fakeManager.ComposeUpCalls >= 1);
        Assert.True(fakeManager.ComposeDownCalls >= 1);
        Assert.False(await db.DynamicFlagInstances.IgnoreQueryFilters().Where(f => f.ValueSecret == "old").Select(f => f.IsActive).SingleAsync());
        Assert.Equal(1, await db.DynamicFlagInstances.IgnoreQueryFilters().CountAsync(f => f.TeamId == teamId && f.IsActive));
    }

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new PenetrationInstanceTenantContext(competitionId));
    }

    private static void SeedCompetition(ApplicationDbContext db, Guid competitionId)
        => db.Competitions.Add(new Competition
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "CTF",
            OwnerId = Guid.NewGuid(),
            GameModeType = GameModeType.Ctf,
            ModeKey = "ctf",
            Status = CompetitionStatus.Running,
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = DateTime.UtcNow.AddHours(1)
        });

    private static void SeedChallenge(ApplicationDbContext db, Guid competitionId, Guid challengeId)
        => db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            CompetitionId = competitionId,
            Title = "Range",
            TypeId = PenetrationConstants.TypeId,
            FlagPrefix = "flag",
            PenetrationConfigJson = JsonSerializer.Serialize(new PenetrationRuntimeConfig
            {
                AllowReset = true,
                MaxResetCount = 3,
                InstanceTtlSeconds = 3600,
                ActionCooldownSeconds = 60,
                VisibleEntryAfterStart = true
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CreatedAt = DateTime.UtcNow
        });

    private sealed class FakeComposeContainerManager(Guid nodeId) : IContainerManager
    {
        public int ComposeUpCalls { get; private set; }
        public int ComposeDownCalls { get; private set; }

        public Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DestroyContainerAsync(ContainerInstance container, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken cancellationToken = default)
        {
            ComposeUpCalls++;
            return Task.FromResult(new ComposeDeployment(
                Guid.NewGuid(),
                Guid.Empty,
                null,
                null,
                "fake",
                config.ProjectName,
                config.ComposeYaml,
                "running",
                DateTime.UtcNow));
        }

        public Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken cancellationToken = default)
        {
            ComposeDownCalls++;
            return Task.CompletedTask;
        }

        public Task<ComposeStatus> GetComposeStatusAsync(
            string projectName,
            Dictionary<string, string>? labels = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ComposeStatus(
                projectName,
                "running",
                [
                    new ComposeServiceInstance(
                        "web",
                        "container-1",
                        "running",
                        nodeId,
                        new Dictionary<int, int> { [80] = 32080 })
                ]));
    }
}

file class PenetrationInstanceTenantContext(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
