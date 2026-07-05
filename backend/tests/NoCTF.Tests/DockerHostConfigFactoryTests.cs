using NoCTF.Container.Docker;
using NoCTF.PluginBase;

namespace NoCTF.Tests;

public class DockerHostConfigFactoryTests
{
    [Fact]
    public void Create_DefaultPolicyKeepsDockerDefaultCapabilities()
    {
        var hostConfig = DockerHostConfigFactory.Create(new ContainerConfig("example/challenge:latest"), publishAllPorts: false);

        Assert.Empty(hostConfig.CapDrop);
        Assert.Empty(hostConfig.CapAdd);
        Assert.Contains("no-new-privileges:true", hostConfig.SecurityOpt);
    }

    [Fact]
    public void Create_ExplicitPolicyCanDropAllCapabilities()
    {
        var hostConfig = DockerHostConfigFactory.Create(
            new ContainerConfig(
                "example/challenge:latest",
                SecurityPolicy: new ContainerSecurityPolicy(CapDrop: ["ALL"])),
            publishAllPorts: false);

        Assert.Equal(["ALL"], hostConfig.CapDrop);
    }
}
