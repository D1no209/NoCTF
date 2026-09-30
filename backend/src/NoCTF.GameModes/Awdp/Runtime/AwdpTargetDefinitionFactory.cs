using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.GameModes.Awdp.Runtime;

using NoCTF.GameModes.PatchVerification.Runtime;

public static class AwdpTargetDefinitionFactory
{
    public static ContainerRuntimeRequest Create(
        Guid operationId,
        ChallengeRuntimeTemplate template,
        RuntimeProvider provider,
        DateTimeOffset now)
        => PatchVerificationTargetDefinitionFactory.Create(
            operationId,
            template,
            provider,
            RuntimePurpose.AwdpTarget);
}
