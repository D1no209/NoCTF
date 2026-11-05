using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Challenges.Testing;

public enum TemplateTestUse : short { Interactive, CapabilityVerification }
public enum TemplateCapabilityTestKind : short { Fix, Check, FixAndCheck }
public enum TemplateCapabilityTestState : short { Queued, Applying, Checking, Finalizing, Completed, PlatformFailed, Canceled }

public sealed class TemplateCapabilityTest : IConcurrencyTracked
{
    public Guid RuntimeInstanceId { get; set; }
    public Guid ExecutionId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid ActorUserId { get; set; }
    public Guid RequestId { get; set; }
    public TemplateCapabilityTestKind Kind { get; set; }
    public TemplateCapabilityTestState State { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
    public DateTimeOffset Deadline { get; set; }
    public DateTimeOffset? MutationStartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? FileId { get; set; }
    public CapabilityExecutionFailure? Failure { get; set; }
    public TemplateCapabilityTestState? FailureStage { get; set; }
    public FixResult? FixResult { get; set; }
    public int? FixExitCode { get; set; }
    public bool FixExecutionUncertain { get; set; }
    public bool CheckCompleted { get; set; }
    public bool CancellationRequested { get; set; }
    public DateTimeOffset? CheckObservedAt { get; set; }
    public DateTimeOffset? CheckStartedAt { get; set; }
    public List<TemplateCheckRequirement> Requirements { get; set; } = [];
    public List<TemplateCheckProbe> Probes { get; set; } = [];
    public List<TemplateCheckFlagContext> FlagContexts { get; set; } = [];

    public bool IsTerminal => State is TemplateCapabilityTestState.Completed or TemplateCapabilityTestState.PlatformFailed or TemplateCapabilityTestState.Canceled;

    public void Fail(CapabilityExecutionFailure reason)
    {
        if (Failure is not null) return;
        Failure = reason;
        FailureStage = State;
    }
}

public sealed class TemplateCheckRequirement
{
    public Guid RuntimeInstanceId { get; set; }
    public int Position { get; set; }
    public CheckItemKind Kind { get; set; }
    public Guid? FlagId { get; set; }
}

public sealed class TemplateCheckProbe
{
    public Guid RuntimeInstanceId { get; set; }
    public int Position { get; set; }
    public CheckItemKind Kind { get; set; }
    public Guid? FlagId { get; set; }
    public CheckProbeResult Result { get; set; }
    public CheckFailureReason Reason { get; set; }
}

public sealed class TemplateCheckFlagContext
{
    public Guid RuntimeInstanceId { get; set; }
    public Guid FlagId { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(32768)] public byte[]? ProtectedReadContext { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(2048)] public string? PublicLocator { get; set; }
}
