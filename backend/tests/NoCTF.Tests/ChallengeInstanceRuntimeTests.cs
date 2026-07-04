using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Core;
using NoCTF.PluginBase;

namespace NoCTF.Tests;

public class ChallengeInstanceRuntimeTests
{
    [Fact]
    public void BuildResponse_PrefersRunnerEntryUrlAndPublicHost()
    {
        var now = DateTime.UtcNow;
        var box = new AwdGameBox
        {
            ContainerInstanceId = "noctf-inst-abc",
            PublicHost = "entry.example.test",
            EntryUrl = "https://team-1.k8s.example.test",
            PortMappingsJson = """{"8080":8080}""",
            ExpiresAt = now.AddMinutes(30)
        };

        var response = ChallengeInstanceRuntime.BuildResponse(
            box,
            new ConfigurationBuilder().Build(),
            new DefaultHttpContext(),
            now);

        Assert.Equal("entry.example.test", response.AccessHost);
        Assert.Equal("https://team-1.k8s.example.test", response.EntryUrl);
        Assert.Equal("https://team-1.k8s.example.test", response.Address);
        Assert.Equal(["https://team-1.k8s.example.test"], response.Addresses);
    }

    [Fact]
    public void BuildContainerConfig_InjectsFormattedDynamicFlag()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        var flagUuid = "8481bb12-baa8-43e5-b878-307a5896e831";
        var challenge = new Challenge
        {
            Id = challengeId,
            CompetitionId = competitionId,
            ContainerImage = "registry/challenge:latest",
            ExposedPort = 9999,
            FlagPrefix = "flag",
            FlagEnvironmentVariable = "NOCTF_FLAG_UUID"
        };
        var dynamicFlag = new CtfDynamicFlag
        {
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            TeamId = teamId,
            EnvironmentVariable = "NOCTF_FLAG_UUID",
            FlagUuid = flagUuid
        };

        var config = InvokeBuildContainerConfig(challenge, teamId, dynamicFlag);

        Assert.NotNull(config.EnvironmentVariables);
        Assert.Equal($"flag{{{flagUuid}}}", config.EnvironmentVariables["NOCTF_FLAG_UUID"]);
    }

    private static ContainerConfig InvokeBuildContainerConfig(
        Challenge challenge,
        Guid teamId,
        CtfDynamicFlag dynamicFlag)
    {
        var method = typeof(CreateChallengeInstanceEndpoint).GetMethod(
            "BuildContainerConfig",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<ContainerConfig>(
            method.Invoke(null, new object?[] { challenge, (Guid?)teamId, dynamicFlag }));
    }
}
