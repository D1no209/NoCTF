using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Runtime;

public enum RuntimeKind : short
{
    Container,
    Compose,
    OvaVm
}

public enum RuntimeState : short
{
    Queued,
    Provisioning,
    Running,
    Stopping,
    Stopped,
    Failed
}

public enum RuntimePurpose : short
{
    Player,
    AwdpTarget,
    Practice,
    AwdpAttack
}

public enum AwdpFixStage : short
{
    TargetProvisioning,
    AwaitingPatch,
    PatchApplying,
    CheckerRunning,
    Completed
}

public enum RuntimeFailureCode : short
{
    InvalidConfiguration,
    RunnerUnavailable,
    ProviderUnavailable,
    ProvisionTimeout,
    ProviderRejected,
    CleanupFailed,
    UrlExpansionFailed,
    PublishedPortRangeExhausted
}

public enum RuntimeCleanupResult : short
{
    Pending,
    ResourcesAbsent,
    ResourcesRemain,
    CleanupFailed,
    CapacityOwnershipConflict
}

public sealed class RuntimePublishedPort
{
    [MaxLength(63)]
    public string? ServiceName { get; set; }
    public int ContainerPort { get; set; }
    public int HostPort { get; set; }
    public DateTimeOffset AllocatedAt { get; set; }
}

public sealed class RuntimeInstance
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid? TeamId { get; set; }
    public RuntimePurpose Purpose { get; set; }
    public Guid? GameplayFactId { get; set; }
    public AwdpFixStage? AwdpFixStage { get; set; }
    public int SourceCompetitionConfigurationRevision { get; set; }
    public int SourceCompetitionChallengeRevision { get; set; }
    public int SourceChallengeDefinitionRevision { get; set; }
    public int Generation { get; set; }
    public RuntimeKind RuntimeKind { get; set; }
    public RuntimeProvider RuntimeProvider { get; set; }
    [MaxLength(128)]
    public string? RunnerId { get; set; }
    [MaxLength(256)]
    public string RunnerPool { get; set; } = string.Empty;
    public Guid? RunnerAssignmentReleaseToken { get; set; }
    public RuntimeState State { get; set; }
    public RuntimeFailureCode? FailureCode { get; set; }
    public DateTimeOffset? RunnerUnavailableAt { get; set; }
    public long ProcessingVersion { get; set; }
    public Guid? ReplacesRuntimeInstanceId { get; set; }
    public string? ProviderReceiptJson { get; set; }
    public string[] Urls { get; set; } = [];
    public int[] ParticipantUrlIndexes { get; set; } = [];
    public string? ControlCheckUrl { get; set; }
    [MaxLength(256)]
    public string? AwdCheckerTargetHost { get; set; }
    public AwdServiceState CheckerStatus { get; set; } = AwdServiceState.Unknown;
    public DateTimeOffset? CheckerStatusUpdatedAt { get; set; }
    public long CheckerSequence { get; set; }
    public long LastAppliedCheckerSequence { get; set; }
    public DateTimeOffset? NextCheckerDueAt { get; set; }
    public DateTimeOffset? CheckerDeadlineAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RunningAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public List<RuntimePublishedPort> PublishedPorts { get; set; } = [];
}
