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
    AwdpAttack,
    TemplateTest,
    PatchVerificationTarget
}

public enum RuntimeTestFlagDelivery : short
{
    NotRequired,
    Environment,
    Command
}

public enum RuntimeTestFlagState : short
{
    NotRequired,
    Pending,
    Succeeded,
    Failed,
    Canceled
}

public enum RuntimeFailureCode : short
{
    InvalidConfiguration,
    RunnerUnavailable,
    ProviderUnavailable,
    ProvisionTimeout,
    ProviderRejected,
    CleanupFailed,
    UrlExpansionFailed
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
}

public sealed class RuntimeInstance
{
    public Guid Id { get; set; }
    public Guid? CompetitionId { get; set; }
    public Guid? CompetitionChallengeId { get; set; }
    public Guid? ChallengeId { get; set; }
    public Guid? TeamId { get; set; }
    public RuntimePurpose Purpose { get; set; }
    public RuntimeTestFlagDelivery? TestFlagDelivery { get; set; }
    public RuntimeTestFlagState? TestFlagState { get; set; }
    public Guid? GameplayFactId { get; set; }
    public RuntimeKind RuntimeKind { get; set; }
    public RuntimeProvider RuntimeProvider { get; set; }
    [MaxLength(128)]
    public string? RunnerId { get; set; }
    public RuntimeState State { get; set; }
    public RuntimeFailureCode? FailureCode { get; set; }
    public string? ProviderReceiptJson { get; set; }
    public RuntimeCapacityAllocations CapacityAllocations { get; set; } = RuntimeCapacityAllocations.Empty;
    public string[] Urls { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RunningAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public List<RuntimePublishedPort> PublishedPorts { get; set; } = [];
}
