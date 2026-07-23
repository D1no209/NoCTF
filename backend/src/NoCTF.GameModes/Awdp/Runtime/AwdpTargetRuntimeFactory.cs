using NoCTF.Application.Runtime.Ports;
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
        int generation,
        long submissionProcessingVersion,
        int configurationRevision,
        DateTimeOffset now)
    {
        if (template.RuntimeKind != RuntimeKind.Container
            || template.Provider is not (RuntimeProvider.Docker or RuntimeProvider.Kubernetes))
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
            RuntimeProvider = template.Provider,
            RunnerPool = template.RunnerPool,
            State = RuntimeState.Queued,
            ConfigurationRevision = configurationRevision,
            CreatedAt = now
        };
    }
}
