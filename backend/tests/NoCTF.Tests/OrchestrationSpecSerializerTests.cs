using NoCTF.Core;
using NoCTF.PluginBase;

namespace NoCTF.Tests;

public class OrchestrationSpecSerializerTests
{
    [Fact]
    public void FromLegacy_MigratesDockerComposeFields()
    {
        var json = OrchestrationSpecSerializer.Write(OrchestrationSpecSerializer.FromLegacy(
            "registry/range:latest",
            null,
            8080,
            "services:\n  web:\n    image: nginx:alpine\n",
            "range",
            dockerCompose: true));

        var spec = OrchestrationSpecSerializer.Read(json);

        Assert.Equal(OrchestrationProvider.Docker, spec.Provider);
        Assert.Equal(OrchestrationRuntimeKind.Compose, spec.Runtime);
        Assert.Equal("registry/range:latest", spec.Image);
        Assert.Equal(8080, spec.ExposedPort);
        Assert.Equal(OrchestrationExposureType.NodePort, spec.Kubernetes.Exposure);
    }

    [Fact]
    public void EntityDefaults_AreBackwardCompatible()
    {
        Assert.Equal("{}", new Challenge().OrchestrationJson);
        Assert.Equal("{}", new ChallengeTemplate().OrchestrationJson);
        Assert.Equal("{}", new PenetrationNode().OrchestrationJson);
        Assert.Equal("docker", new AwdGameBox().ProviderType);
    }

    [Fact]
    public void Read_InvalidJson_IsRejected()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            OrchestrationSpecSerializer.Read("{not-json}"));

        Assert.Equal("Orchestration JSON is invalid.", error.Message);
    }
}
