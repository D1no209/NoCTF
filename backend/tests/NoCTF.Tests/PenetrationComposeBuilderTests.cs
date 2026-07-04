using NoCTF.Core;
using NoCTF.Plugins.Penetration;

namespace NoCTF.Tests;

public class PenetrationComposeBuilderTests
{
    [Fact]
    public void Build_InjectsDynamicFlagsAndUsesRandomHostPortSyntax()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var topologyId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var flagId = Guid.NewGuid();

        var challenge = new Challenge
        {
            Id = challengeId,
            CompetitionId = competitionId,
            Title = "Range",
            TypeId = PenetrationConstants.TypeId,
            FlagPrefix = "flag"
        };
        var instance = new TeamChallengeInstance
        {
            Id = instanceId,
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            ComposeProjectName = "noctf-pen-test"
        };
        var node = new PenetrationNode
        {
            Id = nodeId,
            CompetitionId = competitionId,
            TopologyId = topologyId,
            Name = "web",
            Image = "registry/range-web:latest",
            EnvironmentJson = "{\"APP_ENV\":\"prod\"}",
            PortsJson = "[8080]",
            IsEntry = true,
            IsInternal = false,
            DisplayOrder = 1
        };
        var flag = new PenetrationFlag
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
            InjectionKey = "NOCTF_STAGE1"
        };
        var dynamicFlag = new DynamicFlagInstance
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            TeamId = teamId,
            FlagId = flagId,
            InstanceId = instanceId,
            ValueSecret = "[REDACTED]",
            PlainValue = "abc-123",
            ValueHash = PenetrationFlagService.Hash("abc-123"),
            IsActive = true,
            GeneratedAt = DateTime.UtcNow
        };

        var result = new PenetrationComposeBuilder().Build(challenge, instance, [node], [flag], [dynamicFlag]);

        Assert.Equal(node, result.EntryNode);
        Assert.Equal(8080, result.EntryContainerPort);
        Assert.Contains("NOCTF_STAGE1: \"flag{abc-123}\"", result.ComposeYaml);
        Assert.DoesNotContain("abc-123", result.RedactedComposeYaml);
        Assert.Contains("NOCTF_STAGE1: \"[REDACTED]\"", result.RedactedComposeYaml);
        Assert.Contains("APP_ENV: \"prod\"", result.ComposeYaml);
        Assert.Contains("APP_ENV: \"[REDACTED]\"", result.RedactedComposeYaml);
        Assert.Contains("- \"8080\"", result.ComposeYaml);
        Assert.DoesNotContain("0:8080", result.ComposeYaml);
        Assert.Contains("no-new-privileges:true", result.ComposeYaml);
        Assert.Contains("cap_drop:", result.ComposeYaml);
        Assert.Contains($"instanceId: \"{instanceId}\"", result.ComposeYaml);
    }

    [Fact]
    public void GetPrimaryContainerPort_UsesEntryDefaultWhenNoPortConfigured()
    {
        var entry = new PenetrationNode { IsEntry = true, PortsJson = "[]" };
        var internalNode = new PenetrationNode { IsEntry = false, PortsJson = "[]" };

        Assert.Equal(80, PenetrationComposeBuilder.GetPrimaryContainerPort(entry, null));
        Assert.Equal(9000, PenetrationComposeBuilder.GetPrimaryContainerPort(entry, 9000));
        Assert.Equal(0, PenetrationComposeBuilder.GetPrimaryContainerPort(internalNode, null));
    }
}
