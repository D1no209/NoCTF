namespace NoCTF.PluginBase;

public record ContainerConfig(
    string Image,
    string? Command = null,
    Dictionary<string, string>? EnvironmentVariables = null,
    Dictionary<string, string>? Labels = null,
    Dictionary<int, int>? PortMappings = null,
    string? NetworkName = null,
    string? RegistryAuth = null,
    TimeSpan? Ttl = null
);

public record ContainerInstance(
    Guid Id,
    Guid CompetitionId,
    Guid? TeamId,
    Guid? ChallengeId,
    string ProviderType,
    string ContainerId,
    Dictionary<int, int> PortMappings,
    string Status,
    DateTime StartedAt,
    DateTime? ExpectedStopAt = null
);

public record GameContext(
    Guid CompetitionId,
    GameModeType GameMode,
    DateTime StartTime,
    DateTime EndTime,
    IReadOnlyDictionary<string, string> Configuration
);

public record SubmissionContext(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    Guid UserId,
    string FlagContent,
    string IpAddress
);

public record ChallengeContext(
    Guid ChallengeId,
    Guid CompetitionId,
    string FlagSecret,
    IReadOnlyDictionary<string, string> Configuration
);
