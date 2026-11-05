using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Challenges.Capabilities;

public enum CapabilityExecutionState : short { Completed, PlatformFailed, NotExecuted }
public enum CapabilityExecutionFailure : short
{
    InvalidInput, RuntimeUnavailable, TargetSuperseded, ExecutionInterrupted, DeadlineExceeded,
    InvalidEvidence, MissingEvidence, InvalidCallback, ResourceCleanupFailed, InputPreparationFailed, TargetExecutionFailed, ResourceUnavailable
}

[PersistentHierarchy]
public abstract class CapabilityOutcome
{
    protected CapabilityOutcome(ChallengeCapabilityKind kind) => Kind = kind;
    public Guid GameplayFactId { get; set; }
    public ChallengeCapabilityKind Kind { get; private set; }
    public Guid? RuntimeInstanceId { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public CapabilityExecutionState ExecutionState { get; set; }
    public CapabilityExecutionFailure? ExecutionFailure { get; set; }
}

public enum BreakMatchResult : short { NotMatched, Matched, Ambiguous }

[PersistentDiscriminator("break")]
public sealed class BreakOutcome() : CapabilityOutcome(ChallengeCapabilityKind.Break)
{
    public BreakMatchResult Match { get; set; }
    public List<BreakFlagMatch> Flags { get; set; } = [];
}

public sealed class BreakFlagMatch
{
    public Guid GameplayFactId { get; set; }
    public Guid FlagId { get; set; }
    public Guid? OwnerTeamId { get; set; }
}

public enum FixResult : short { Applied, Failed, TimedOut }
public enum FixExecutionPhase : short { Queued, Applying, Checking, Finalizing, Finished }
public enum FixTargetKind : short { Isolated, Persistent }

[PersistentDiscriminator("fix")]
public sealed class FixOutcome() : CapabilityOutcome(ChallengeCapabilityKind.Fix)
{
    public FixResult? Result { get; set; }
    public int? ExitCode { get; set; }
    public bool ExecutionUncertain { get; set; }
    [MaxLength(256)] public string? ExecutionIdentity { get; set; }
}

public sealed class FixExecutionDetails
{
    public Guid GameplayFactId { get; set; }
    public Guid RuntimeInstanceId { get; set; }
    public FixTargetKind TargetKind { get; set; }
    public FixExecutionPhase Phase { get; set; }
    public FixExecutionPhase? FailedPhase { get; set; }
    public bool MutationBlocked { get; set; }
    public DateTimeOffset Deadline { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? MutationStartedAt { get; set; }
    public DateTimeOffset? MutationCompletedAt { get; set; }
    public DateTimeOffset? LastConfirmedRunningAt { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public DateTimeOffset? FinalizedAt { get; set; }
    public DateTimeOffset? WorkCompletedAt { get; set; }
    public DateTimeOffset? JudgedAt { get; set; }
    public CapabilityExecutionFailure? ExecutionFailure { get; set; }
}

public enum CheckProbeResult : short { Passed, TargetFailure, PlatformFailure, Unknown, NotRequired, NotExecuted }
public enum CheckFailureReason : short
{
    None, ConnectionRefused, ConnectTimeout, RequestTimeout, FunctionalCheckFailed,
    VulnerabilityPresent, FlagWriteFailed, FlagReadFailed, CheckerCrashed,
    CheckerTimedOut, CallbackMissing, IncompleteEvidence, InvalidEvidence, TargetSuperseded, PlayerMaintenance, InfrastructureFailure
}

[PersistentDiscriminator("check")]
public sealed class CheckOutcome() : CapabilityOutcome(ChallengeCapabilityKind.Check)
{
    public List<CheckProbe> Probes { get; set; } = [];
    public List<CheckFlagContext> FlagContexts { get; set; } = [];
}

public sealed class CheckFlagContext
{
    public Guid GameplayFactId { get; set; }
    public Guid FlagId { get; set; }
    [MaxLength(32768)] public byte[]? ProtectedReadContext { get; set; }
    [MaxLength(2048)] public string? PublicLocator { get; set; }
}

public sealed class CheckProbe
{
    public Guid GameplayFactId { get; set; }
    public int Position { get; set; }
    public CheckItemKind Kind { get; set; }
    public Guid? FlagId { get; set; }
    public CheckProbeResult Result { get; set; }
    public CheckFailureReason Reason { get; set; }
}

public enum ControlResult : short { Controlled, Uncontrolled, Indeterminate }

[PersistentDiscriminator("control")]
public sealed class ControlOutcome() : CapabilityOutcome(ChallengeCapabilityKind.Control)
{
    public ControlResult Result { get; set; }
    [MaxLength(4096)] public string? ControllerIdentifier { get; set; }
    public override string ToString() => $"ControlOutcome {{ GameplayFactId = {GameplayFactId}, Result = {Result}, ControllerIdentifier = [REDACTED] }}";
}

public enum QuestionnaireAnswerResult : short { Correct, Wrong, Unanswered, FullCredit }

[PersistentDiscriminator("questionnaire")]
public sealed class QuestionnaireOutcome() : CapabilityOutcome(ChallengeCapabilityKind.Questionnaire)
{
    public Guid RevisionId { get; set; }
    public List<QuestionnaireQuestionResult> Questions { get; set; } = [];
}

public sealed class QuestionnaireQuestionResult
{
    public Guid GameplayFactId { get; set; }
    public Guid QuestionId { get; set; }
    public QuestionnaireAnswerResult Result { get; set; }
}
