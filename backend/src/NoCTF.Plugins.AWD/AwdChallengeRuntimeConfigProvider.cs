using NoCTF.Core;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.AWD;

public sealed class AwdChallengeRuntimeConfigProvider
{
    public ContainerConfig? Build(Challenge challenge)
    {
        if (challenge.ContainerMode != ChallengeContainerMode.SingleImage)
            throw new InvalidOperationException("AWD currently requires a single-image challenge runtime.");

        var spec = OrchestrationSpecSerializer.Read(challenge.OrchestrationJson);
        var image = string.IsNullOrWhiteSpace(spec.Image) ? challenge.ContainerImage : spec.Image;
        if (string.IsNullOrWhiteSpace(image))
            return null;

        var exposedPort = spec.ExposedPort ?? challenge.ExposedPort;
        return new ContainerConfig(
            Image: image,
            Command: spec.Command,
            EnvironmentVariables: new Dictionary<string, string>(spec.Environment, StringComparer.Ordinal),
            PortMappings: exposedPort is > 0
                ? new Dictionary<int, int> { [exposedPort.Value] = 0 }
                : null,
            Entrypoint: spec.Entrypoint,
            OrchestrationJson: challenge.OrchestrationJson);
    }
}
