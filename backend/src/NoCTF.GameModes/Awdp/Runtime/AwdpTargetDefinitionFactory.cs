using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.GameModes.Awdp.Runtime;

using NoCTF.Application.Challenges.Capabilities;

public static class AwdpTargetDefinitionFactory
{
    public static ContainerRuntimeRequest Create(
        Guid operationId,
        ChallengeRuntimeTemplate template,
        RuntimeProvider provider,
        DateTimeOffset now)
        => FixTargetDefinitionFactory.Create(
            operationId,
            template,
            provider,
            RuntimePurpose.FixTarget);
}
