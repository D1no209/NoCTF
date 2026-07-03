namespace NoCTF.PluginBase;

public record ContainerConfig(
    string Image,
    string? Command = null,
    Dictionary<string, string>? EnvironmentVariables = null,
    Dictionary<string, string>? Labels = null,
    Dictionary<int, int>? PortMappings = null,
    string? NetworkName = null,
    string? RegistryAuth = null,
    TimeSpan? Ttl = null,
    ContainerResourceLimits? ResourceLimits = null,
    ContainerSecurityPolicy? SecurityPolicy = null,
    IReadOnlyList<string>? Entrypoint = null
);

public record ContainerResourceLimits(
    long MemoryBytes = 268435456,
    long NanoCpus = 500000000,
    long PidsLimit = 128
);

public record ContainerSecurityPolicy(
    bool NoNewPrivileges = true,
    bool ReadonlyRootfs = false,
    bool RunAsNonRoot = true,
    IReadOnlyList<string>? CapDrop = null,
    IReadOnlyList<string>? CapAdd = null
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

public record ContainerRunResult(
    string ContainerId,
    int ExitCode,
    string? StdOut,
    string? StdErr,
    DateTime StartedAt,
    DateTime FinishedAt
);

public record ComposeConfig(
    string ProjectName,
    string ComposeYaml,
    Dictionary<string, string>? EnvironmentVariables = null,
    Dictionary<string, string>? Labels = null,
    TimeSpan? Ttl = null
);

public record ComposeDeployment(
    Guid Id,
    Guid CompetitionId,
    Guid? TeamId,
    Guid? ChallengeId,
    string ProviderType,
    string ProjectName,
    string ComposeYaml,
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
