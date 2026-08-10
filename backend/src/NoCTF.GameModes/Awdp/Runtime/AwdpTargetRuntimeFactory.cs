using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.GameModes.Awdp.Runtime;

public static class AwdpTargetRuntimeFactory
{
    public static RuntimeInstance Create(
        Guid gameplayFactId,
        Guid competitionId,
        Guid competitionChallengeId,
        Guid runtimeInstanceId,
        ChallengeRuntimeTemplate template,
        RuntimePlacement placement,
        int generation,
        int competitionConfigurationRevision,
        int competitionChallengeRevision,
        int challengeDefinitionRevision,
        DateTimeOffset now)
    {
        if (template.Definition is not ContainerRuntimeDefinition)
            throw new InvalidOperationException(
                "AWDP disposable targets require a Docker or Kubernetes Container runtime.");
        if (generation < 1)
            throw new ArgumentOutOfRangeException(nameof(generation));
        if (competitionConfigurationRevision < 0
            || competitionChallengeRevision < 0
            || challengeDefinitionRevision < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(competitionConfigurationRevision),
                "AWDP target source revisions cannot be negative.");
        }

        return new RuntimeInstance
        {
            Id = runtimeInstanceId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = null,
            Purpose = RuntimePurpose.AwdpTarget,
            GameplayFactId = gameplayFactId,
            SourceCompetitionConfigurationRevision = competitionConfigurationRevision,
            SourceCompetitionChallengeRevision = competitionChallengeRevision,
            SourceChallengeDefinitionRevision = challengeDefinitionRevision,
            Generation = generation,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = placement.Provider,
            RunnerPool = placement.RunnerPool,
            State = RuntimeState.Queued,
            CreatedAt = now
        };
    }
}
