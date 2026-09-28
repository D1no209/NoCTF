using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NoCTF.Domain.Shared;

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

public enum RuntimeAccessMode : short
{
    Direct,
    DirectAndWsrx,
    WsrxOnly
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
    public Guid Id { get; set; }
    [MaxLength(63)]
    public string? ServiceName { get; set; }
    public int ContainerPort { get; set; }
    public int HostPort { get; set; }
}

public sealed class RuntimeAccessEndpoint
{
    public int BindingIndex { get; set; }
    public string? DirectAddress { get; set; }
    [MaxLength(255)]
    public string? TargetHost { get; set; }
    public int? TargetPort { get; set; }
}

public sealed class RuntimeCapacityAllocationEntry
{
    public Guid Id { get; set; }
    public RuntimeWorkloadKind WorkloadKind { get; set; }
    public Guid WorkloadRuntimeInstanceId { get; set; }
    public Guid OperationId { get; set; }
    public Guid? GameplayFactId { get; set; }
    [MaxLength(256)] public string ResourceDomain { get; set; } = string.Empty;
    [MaxLength(128)] public string RunnerId { get; set; } = string.Empty;
    public long BudgetMemoryBytes { get; set; }
    public long BudgetNanoCpus { get; set; }
    public long BudgetPidsLimit { get; set; }
    public long LimitMemoryBytes { get; set; }
    public long LimitNanoCpus { get; set; }
    public long LimitPidsLimit { get; set; }

    public static RuntimeCapacityAllocationEntry FromValue(RuntimeCapacityAllocation value) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkloadKind = value.Identity.Kind,
        WorkloadRuntimeInstanceId = value.Identity.RuntimeInstanceId,
        OperationId = value.Identity.OperationId,
        GameplayFactId = value.GameplayFactId,
        ResourceDomain = value.ResourceDomain,
        RunnerId = value.RunnerId,
        BudgetMemoryBytes = value.Budget.MemoryBytes,
        BudgetNanoCpus = value.Budget.NanoCpus,
        BudgetPidsLimit = value.Budget.PidsLimit,
        LimitMemoryBytes = value.Limit.MemoryBytes,
        LimitNanoCpus = value.Limit.NanoCpus,
        LimitPidsLimit = value.Limit.PidsLimit
    };

    public RuntimeCapacityAllocation ToValue() => new(
        new(WorkloadKind, WorkloadRuntimeInstanceId, OperationId),
        GameplayFactId,
        ResourceDomain,
        RunnerId,
        new(BudgetMemoryBytes, BudgetNanoCpus, BudgetPidsLimit),
        new(LimitMemoryBytes, LimitNanoCpus, LimitPidsLimit));
}

[PersistentHierarchy]
[GeneratePersistentLeaves(typeof(RuntimePurpose), "RuntimeInstance")]
public abstract class RuntimeInstance : IConcurrencyTracked
{
    protected RuntimeInstance(RuntimePurpose purpose) => Purpose = purpose;
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public ActiveRuntimeSlot? ActiveSlot { get; set; }
    public Guid? CompetitionId { get; set; }
    public Guid? CompetitionChallengeId { get; set; }
    public Guid? ChallengeId { get; set; }
    public Guid? TeamId { get; set; }
    public RuntimePurpose Purpose { get; private set; }
    public RuntimeAccessMode AccessMode { get; set; }
    public bool TrafficCaptureEnabled { get; set; }
    public long? TrafficCaptureLimitBytes { get; set; }
    public long TrafficCaptureReservedBytes { get; set; }
    public RuntimeTestFlagDelivery? TestFlagDelivery { get; set; }
    public RuntimeTestFlagState? TestFlagState { get; set; }
    public Guid? GameplayFactId { get; set; }
    public RuntimeKind RuntimeKind { get; set; }
    public RuntimeProvider RuntimeProvider { get; set; }
    [MaxLength(128)]
    public string? RunnerId { get; set; }
    public RuntimeState State { get; set; }
    public RuntimeFailureCode? FailureCode { get; set; }
    public RuntimeReceipt? ProviderReceipt { get; set; }
    public List<RuntimeCapacityAllocationEntry> CapacityAllocationEntries { get; set; } = [];
    [NotMapped]
    public RuntimeCapacityAllocations CapacityAllocations
    {
        get => new(CapacityAllocationEntries.Select(entry => entry.ToValue()).ToArray());
    }
    public List<RuntimeAccessEndpoint> AccessEndpoints { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RunningAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public List<RuntimePublishedPort> PublishedPorts { get; set; } = [];
}
