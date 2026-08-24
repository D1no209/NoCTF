using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.GameModes.Awdp.Runtime;

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
    {
        if (template.Definition is not ContainerRuntimeDefinition)
            throw new InvalidOperationException(
                "AWDP disposable targets require a Docker or Kubernetes Container runtime.");
        return new RuntimeInstance
        {
            Id = runtimeInstanceId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            Purpose = RuntimePurpose.AwdpTarget,
            GameplayFactId = gameplayFactId,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = placement.Provider,
            State = RuntimeState.Queued,
            CreatedAt = now
        };
    }
}
