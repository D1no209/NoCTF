using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Plugins.Penetration;

namespace NoCTF.Tests;

public class PenetrationTopologyServiceTests
{
    [Fact]
    public async Task SaveTemplateTopology_ClonesNodesFlagsAndRuntimeConfigToChallenge()
    {
        var competitionId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedTemplate(db, templateId);
        SeedCompetition(db, competitionId);
        SeedChallenge(db, competitionId, challengeId, templateId);
        await db.SaveChangesAsync();

        var service = new PenetrationTopologyService(db);
        var saved = await service.SaveTemplateTopologyAsync(templateId, ValidDocument(maxResetCount: 7), CancellationToken.None);
        var cloned = await service.CloneTemplateToChallengeAsync(templateId, challengeId, CancellationToken.None);

        var template = await db.ChallengeTemplates.SingleAsync(t => t.Id == templateId);
        var challenge = await db.Challenges.IgnoreQueryFilters().SingleAsync(c => c.Id == challengeId);
        var nodes = await db.PenetrationNodes.IgnoreQueryFilters().Where(n => n.CompetitionId == competitionId).ToListAsync();
        var flags = await db.PenetrationFlags.IgnoreQueryFilters().Where(f => f.CompetitionId == competitionId).ToListAsync();

        Assert.Equal(ChallengeDeploymentType.DynamicContainer, template.DeploymentType);
        Assert.Equal(ChallengeContainerMode.DockerCompose, template.ContainerMode);
        Assert.Equal(ChallengeDeploymentType.DynamicContainer, challenge.DeploymentType);
        Assert.Equal(ChallengeContainerMode.DockerCompose, challenge.ContainerMode);
        Assert.Equal(7, saved.Config.MaxResetCount);
        Assert.Equal(7, cloned.Config.MaxResetCount);
        Assert.Equal(2, nodes.Count);
        Assert.Equal(2, flags.Count);
        Assert.Contains(flags, f => f.Stage == 1 && f.IsDynamic && f.InjectionKey == "NOCTF_STAGE1");
        Assert.Contains(flags, f => f.Stage == 2 && !f.IsDynamic && f.ValueHash == PenetrationTopologyService.HashSecret("rooted"));
    }

    [Fact]
    public async Task GetTemplateTopology_ReturnsPersistedRuntimeConfig()
    {
        var competitionId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedTemplate(db, templateId);
        await db.SaveChangesAsync();

        var service = new PenetrationTopologyService(db);
        await service.SaveTemplateTopologyAsync(templateId, ValidDocument(maxResetCount: 5), CancellationToken.None);

        var topology = await service.GetTemplateTopologyAsync(templateId, CancellationToken.None);

        Assert.Equal("ok", topology.Code);
        Assert.Equal(5, topology.Config.MaxResetCount);
        Assert.Equal(3600, topology.Config.InstanceTtlSeconds);
    }

    [Fact]
    public async Task SaveTemplateTopology_RejectsPublishedPortsOnInternalNodes()
    {
        var competitionId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        SeedTemplate(db, templateId);
        await db.SaveChangesAsync();

        var document = ValidDocument();
        document.Nodes[1].Ports = Json("[\"5432:5432\"]");
        var service = new PenetrationTopologyService(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveTemplateTopologyAsync(templateId, document, CancellationToken.None));
        Assert.Equal("internal_node_cannot_publish_ports", ex.Message);
    }

    private static PenetrationTopologyDocument ValidDocument(int maxResetCount = 3) => new()
    {
        Name = "Range",
        EntryConfig = Json("{\"scheme\":\"http\"}"),
        Config = new PenetrationRuntimeConfig
        {
            MaxResetCount = maxResetCount,
            InstanceTtlSeconds = 3600,
            ActionCooldownSeconds = 0
        },
        Nodes =
        [
            new PenetrationNodeDocument
            {
                Name = "web",
                Role = "entry",
                Image = "nginx:alpine",
                Ports = Json("[80]"),
                IsEntry = true,
                IsInternal = false,
                DisplayOrder = 1
            },
            new PenetrationNodeDocument
            {
                Name = "db",
                Role = "internal",
                Image = "postgres:16-alpine",
                Ports = Json("[5432]"),
                IsEntry = false,
                IsInternal = true,
                DisplayOrder = 2
            }
        ],
        Flags =
        [
            new PenetrationFlagDocument
            {
                Stage = 1,
                Name = "Initial access",
                Score = 100,
                IsDynamic = true,
                NodeName = "web",
                InjectionKey = "NOCTF_STAGE1"
            },
            new PenetrationFlagDocument
            {
                Stage = 2,
                Name = "Privilege escalation",
                Score = 200,
                IsDynamic = false,
                NodeName = "db",
                ValueSecret = "rooted"
            }
        ]
    };

    private static JsonElement Json(string value)
    {
        using var doc = JsonDocument.Parse(value);
        return doc.RootElement.Clone();
    }

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new PenetrationFixedTenantContext(competitionId));
    }

    private static void SeedTemplate(ApplicationDbContext db, Guid templateId)
        => db.ChallengeTemplates.Add(new ChallengeTemplate
        {
            Id = templateId,
            Title = "Penetration Template",
            TypeId = PenetrationConstants.TypeId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

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
            EndTime = DateTime.UtcNow.AddHours(2)
        });

    private static void SeedChallenge(ApplicationDbContext db, Guid competitionId, Guid challengeId, Guid templateId)
        => db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            CompetitionId = competitionId,
            TemplateId = templateId,
            Title = "Penetration",
            TypeId = PenetrationConstants.TypeId,
            CreatedAt = DateTime.UtcNow
        });
}

file class PenetrationFixedTenantContext(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
