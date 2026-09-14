using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.GameModes.Awdp.Runtime;

using NoCTF.GameModes.PatchVerification.Runtime;

public static class AwdpTargetRuntimeFactory
{
    public static RuntimeInstance Create(
        Guid teamId,
        Guid? gameplayFactId,
        Guid competitionId,
        Guid competitionChallengeId,
        Guid runtimeInstanceId,
        ChallengeRuntimeTemplate template,
        RuntimePlacement placement,
        DateTimeOffset now)
        => PatchVerificationTargetRuntimeFactory.Create(
            teamId,
            gameplayFactId,
            competitionId,
            competitionChallengeId,
            runtimeInstanceId,
            template,
            placement,
            RuntimePurpose.AwdpTarget,
            now);
}
