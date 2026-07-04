using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Core;

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
}
