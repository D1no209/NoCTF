namespace NoCTF.Container.Docker;

public record DockerContainerMetadata(
    string ContainerId,
    string Image,
    string Status,
    Dictionary<int, int> Ports,
    string? NetworkName = null,
    DateTime? StartedAt = null,
    Guid? CompetitionId = null,
    Guid? TeamId = null,
    Guid? ChallengeId = null
);
