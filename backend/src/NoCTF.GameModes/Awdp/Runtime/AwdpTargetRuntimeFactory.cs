using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.GameModes.Awdp.Runtime;

public static class AwdpTargetRuntimeFactory
{
    public static RuntimeInstance Create(
        Guid submissionId,
        Guid competitionId,
        Guid competitionChallengeId,
        Guid runtimeInstanceId,
        ChallengeRuntimeTemplate template,
        RuntimePlacement placement,
        int generation,
        long submissionProcessingVersion,
        DateTimeOffset now)
    {
        if (template.Definition is not ContainerRuntimeDefinition)
            throw new InvalidOperationException(
                "AWDP disposable targets require a Docker or Kubernetes Container runtime.");
        if (generation < 1)
            throw new ArgumentOutOfRangeException(nameof(generation));

        return new RuntimeInstance
        {
            Id = runtimeInstanceId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = null,
            Purpose = RuntimePurpose.AwdpTarget,
            SubmissionId = submissionId,
            SubmissionProcessingVersion = submissionProcessingVersion,
            Generation = generation,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = placement.Provider,
            RunnerPool = placement.RunnerPool,
            State = RuntimeState.Queued,
            CreatedAt = now
        };
    }
}
