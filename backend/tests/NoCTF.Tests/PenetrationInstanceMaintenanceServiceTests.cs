using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Plugins.Penetration;

namespace NoCTF.Tests;

public class PenetrationInstanceMaintenanceServiceTests
{
    [Fact]
    public async Task MaintainAsync_ExpiresInstancesAndDeactivatesDynamicFlags()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var flagId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedChallenge(db, competitionId, challengeId);
        db.TeamChallengeInstances.Add(new TeamChallengeInstance
        {
            Id = instanceId,
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            Status = PenetrationInstanceStatus.Running,
            ComposeProjectName = "range-expired",
            RenderedComposeYaml = "services:\n  web:\n    image: nginx:alpine\n",
            ContainerIdsJson = "[\"container-1\"]",
            EntryPort = 32080,
            EntryUrl = "http://ctf.local:32080",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddHours(-1),
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
            ValueSecret = "secret-value",
            ValueHash = PenetrationFlagService.Hash("secret-value"),
            IsActive = true,
            GeneratedAt = DateTime.UtcNow.AddMinutes(-10)
        });
        await db.SaveChangesAsync();

        var manager = new FakeComposeContainerManager();
        var result = await CreateService(db, manager).MaintainAsync();

        var instance = await db.TeamChallengeInstances.IgnoreQueryFilters().SingleAsync(i => i.Id == instanceId);
        var dynamicFlag = await db.DynamicFlagInstances.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(new InstanceMaintenanceResult(1, 0, 0), result);
        Assert.Equal(PenetrationInstanceStatus.Expired, instance.Status);
        Assert.Null(instance.EntryPort);
        Assert.Null(instance.EntryUrl);
        Assert.False(dynamicFlag.IsActive);
        Assert.Equal(1, manager.ComposeDownCalls);
        Assert.DoesNotContain("secret-value", await db.CompetitionLogs.IgnoreQueryFilters().Select(l => l.MetadataJson).SingleAsync());
    }

    [Fact]
    public async Task MaintainAsync_SyncsRunningInstancePortFromComposeStatus()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var topologyId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
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
            PortsJson = "[8080]",
            IsEntry = true,
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
            ComposeProjectName = "range-running",
            RenderedComposeYaml = "services:\n  web:\n    image: nginx:alpine\n",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
        });
        await db.SaveChangesAsync();

        var manager = new FakeComposeContainerManager
        {
            Status = new ComposeStatus(
                "range-running",
                "running",
                [
                    new ComposeServiceInstance(
                        "web",
                        "container-1",
                        "running",
                        nodeId,
                        new Dictionary<int, int> { [8080] = 32080 })
                ])
        };

        var result = await CreateService(db, manager).MaintainAsync();

        var instance = await db.TeamChallengeInstances.IgnoreQueryFilters().SingleAsync(i => i.Id == instanceId);
        Assert.Equal(new InstanceMaintenanceResult(0, 1, 0), result);
        Assert.Equal(PenetrationInstanceStatus.Running, instance.Status);
        Assert.Equal(32080, instance.EntryPort);
        Assert.Equal("http://ctf.local:32080", instance.EntryUrl);
        Assert.Contains("container-1", instance.ContainerIdsJson);
    }

    [Fact]
    public async Task MaintainAsync_MarksStuckBusyInstanceFailed()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedChallenge(db, competitionId, challengeId);
        db.TeamChallengeInstances.Add(new TeamChallengeInstance
        {
            Id = instanceId,
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            Status = PenetrationInstanceStatus.Starting,
            ComposeProjectName = "range-starting",
            RenderedComposeYaml = "services:\n  web:\n    image: nginx:alpine\n",
            EntryPort = 32080,
            EntryUrl = "http://ctf.local:32080",
            LastActionAt = DateTime.UtcNow.AddMinutes(-20),
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-20),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-20)
        });
        await db.SaveChangesAsync();

        var manager = new FakeComposeContainerManager();
        var result = await CreateService(db, manager).MaintainAsync();

        var instance = await db.TeamChallengeInstances.IgnoreQueryFilters().SingleAsync(i => i.Id == instanceId);
        Assert.Equal(new InstanceMaintenanceResult(0, 0, 1), result);
        Assert.Equal(PenetrationInstanceStatus.Failed, instance.Status);
        Assert.Contains("Starting", instance.LastError);
        Assert.Equal(1, manager.ComposeDownCalls);
        Assert.Equal("penetration.instance.maintenance_timeout", await db.CompetitionLogs.IgnoreQueryFilters().Select(l => l.EventType).SingleAsync());
    }

    private static PenetrationInstanceMaintenanceService CreateService(ApplicationDbContext db, IContainerManager manager)
        => new(
            db,
            manager,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["InstanceAccess:PublicHost"] = "ctf.local"
                })
                .Build(),
            NullLogger<PenetrationInstanceMaintenanceService>.Instance);

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new PenetrationMaintenanceTenantContext(competitionId));
    }

    private static void SeedChallenge(ApplicationDbContext db, Guid competitionId, Guid challengeId)
    {
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
            EndTime = DateTime.UtcNow.AddHours(1)
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            CompetitionId = competitionId,
            Title = "Range",
            TypeId = PenetrationConstants.TypeId,
            ExposedPort = 8080,
            CreatedAt = DateTime.UtcNow
        });
    }

    private sealed class FakeComposeContainerManager : IContainerManager
    {
        public ComposeStatus Status { get; set; } = new("range", "not_found", []);
        public int ComposeDownCalls { get; private set; }

        public Task<ContainerInstance> CreateContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DestroyContainerAsync(ContainerInstance container, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ContainerRunResult> RunContainerAsync(ContainerConfig config, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ComposeDeployment> ComposeUpAsync(ComposeConfig config, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ComposeDownAsync(ComposeDeployment deployment, CancellationToken cancellationToken = default)
        {
            ComposeDownCalls++;
            return Task.CompletedTask;
        }

        public Task<ComposeStatus> GetComposeStatusAsync(
            string projectName,
            Dictionary<string, string>? labels = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Status with { ProjectName = projectName });
    }
}

file class PenetrationMaintenanceTenantContext(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
