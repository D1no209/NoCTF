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
    public void SupportsPlayerManagedInstance_UsesExactGameModeValues()
    {
        Assert.True(ChallengeInstanceRuntime.SupportsPlayerManagedInstance(GameModeType.Ctf));
        Assert.True(ChallengeInstanceRuntime.SupportsPlayerManagedInstance(GameModeType.Awdp));
        Assert.False(ChallengeInstanceRuntime.SupportsPlayerManagedInstance(GameModeType.Awd));
        Assert.False(ChallengeInstanceRuntime.SupportsPlayerManagedInstance(GameModeType.Koh));
        Assert.False(ChallengeInstanceRuntime.SupportsPlayerManagedInstance((GameModeType)999));
    }

    [Theory]
    [InlineData(29, true)]
    [InlineData(30, false)]
    [InlineData(31, false)]
    public void IsCoolingDown_AwdpUsesThirtySecondBoundary(int elapsedSeconds, bool expected)
    {
        var now = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);
        var box = new AwdGameBox { LastInstanceActionAt = now.AddSeconds(-elapsedSeconds) };

        var coolingDown = ChallengeInstanceRuntime.IsCoolingDown(box, now, GameModeType.Awdp);

        Assert.Equal(expected, coolingDown);
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void IsCoolingDown_CtfKeepsExistingFiveSecondBoundary(int elapsedSeconds, bool expected)
    {
        var now = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);
        var box = new AwdGameBox { LastInstanceActionAt = now.AddSeconds(-elapsedSeconds) };

        var coolingDown = ChallengeInstanceRuntime.IsCoolingDown(box, now, GameModeType.Ctf);

        Assert.Equal(expected, coolingDown);
    }

    [Fact]
    public void BuildResponse_AwdpPublishesThirtySecondCooldown()
    {
        var now = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);
        var box = new AwdGameBox { LastInstanceActionAt = now };

        var response = ChallengeInstanceRuntime.BuildResponse(
            box,
            new ConfigurationBuilder().Build(),
            new DefaultHttpContext(),
            now,
            GameModeType.Awdp);

        Assert.Equal(now.AddSeconds(30), response.CooldownUntil);
    }

    [Fact]
    public void ResolveAccessHost_DoesNotUseUntrustedRequestHost()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("attacker.example.test");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InstanceAccess:TrustRequestHost"] = "false",
                ["ASPNETCORE_ENVIRONMENT"] = "Development"
            })
            .Build();

        var host = ChallengeInstanceRuntime.ResolveAccessHost(configuration, httpContext);

        Assert.Equal(string.Empty, host);
    }

    [Fact]
    public void ResolveAccessHost_UsesRequestHostOnlyWhenExplicitlyTrusted()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("instances.example.test");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["InstanceAccess:TrustRequestHost"] = "true",
                ["ASPNETCORE_ENVIRONMENT"] = "Production"
            })
            .Build();

        var host = ChallengeInstanceRuntime.ResolveAccessHost(configuration, httpContext);

        Assert.Equal("instances.example.test", host);
    }

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

    [Fact]
    public void BuildContainerConfig_MergesOrchestrationEnvironmentWithDynamicFlag()
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
            FlagEnvironmentVariable = "FLAG",
            OrchestrationJson = OrchestrationSpecSerializer.Write(new OrchestrationSpec
            {
                Environment = new Dictionary<string, string>
                {
                    ["GLIBC_TUNABLES"] = "glibc.cpu.hwcaps=-SHSTK,-IBT",
                    ["FLAG"] = "placeholder"
                }
            })
        };
        var dynamicFlag = new CtfDynamicFlag
        {
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            TeamId = teamId,
            EnvironmentVariable = "FLAG",
            FlagUuid = flagUuid
        };

        var config = InvokeBuildContainerConfig(challenge, teamId, dynamicFlag);

        Assert.NotNull(config.EnvironmentVariables);
        Assert.Equal("glibc.cpu.hwcaps=-SHSTK,-IBT", config.EnvironmentVariables["GLIBC_TUNABLES"]);
        Assert.Equal($"flag{{{flagUuid}}}", config.EnvironmentVariables["FLAG"]);
    }

    [Fact]
    public void BuildContainerConfig_UsesPersistedRuntimeOperationId()
    {
        var operationId = Guid.NewGuid();
        var challenge = new Challenge
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            ContainerImage = "registry/challenge:latest"
        };
        var dynamicFlag = new CtfDynamicFlag
        {
            Id = Guid.NewGuid(),
            CompetitionId = challenge.CompetitionId,
            ChallengeId = challenge.Id,
            TeamId = Guid.NewGuid(),
            EnvironmentVariable = "FLAG",
            FlagUuid = Guid.NewGuid().ToString("D")
        };

        var config = InvokeBuildContainerConfig(
            challenge,
            dynamicFlag.TeamId,
            dynamicFlag,
            operationId);

        Assert.Equal(operationId, config.OperationId);
    }

    [Fact]
    public void ClearContainer_ClearsRuntimeOperationReceipt()
    {
        var box = new AwdGameBox
        {
            ContainerInstanceId = "container",
            RuntimeOperationId = Guid.NewGuid()
        };

        ChallengeInstanceRuntime.ClearContainer(box);

        Assert.Null(box.ContainerInstanceId);
        Assert.Null(box.RuntimeOperationId);
    }

    private static ContainerConfig InvokeBuildContainerConfig(
        Challenge challenge,
        Guid teamId,
        CtfDynamicFlag dynamicFlag,
        Guid? operationId = null)
    {
        var method = typeof(CreateChallengeInstanceEndpoint).GetMethod(
            "BuildContainerConfig",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<ContainerConfig>(
            method.Invoke(null, new object?[] { challenge, (Guid?)teamId, dynamicFlag, operationId }));
    }
}
