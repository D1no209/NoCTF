using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Gameplay;

/// <summary>A player, administrator, or system fact together with its current authoritative result.</summary>
public sealed class GameplayFact
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? VictimTeamId { get; set; }
    public Guid? ActorUserId { get; set; }
    public GameplayFactKind Kind { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public GameplayFactReferenceKind? ReferenceKind { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Value { get; set; }
    [MaxLength(32)]
    public byte[]? ValueSha256 { get; set; }
    public GameplayFactState State { get; set; }
    public GameplayFactResult? Result { get; set; }
    public GameplayFactFailureCode? FailureCode { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum GameplayFactKind : short
{
    FlagAttempt,
    BreakAttempt,
    FixAttempt,
    HintUnlock,
    ManualAdjustment,
    AwdServiceTransition,
    KohControlObservation
}

public enum GameplayFactReferenceKind : short
{
    PatchUpload,
    Hint,
    AwdRound
}

public enum GameplayFactState : short
{
    Pending,
    Queued,
    Processing,
    Completed,
    PlatformFailed
}

public enum EvaluationDispatchMode : short
{
    Automatic,
    ManualBatch
}

public enum GameplayFactResult : short
{
    Correct,
    Wrong,
    Duplicate,
    AttemptsExhausted,
    Rejected,
    Unlocked,
    Applied,
    ServiceUp,
    ServiceDown,
    Controlled,
    Uncontrolled
}

public enum GameplayFactFailureCode : short
{
    FlagNotSupported,
    FixNotSupported,
    BreakAttemptsExhausted,
    FixAttemptsExhausted,
    BreakRequired,
    ArchiveValidationUnavailable,
    FixArchiveMissing,
    FixArchiveLengthMismatch,
    FixArchiveContentTypeMismatch,
    FixArchiveHashMismatch,
    StorageTimeout,
    StorageUnavailable,
    CheckerPlatformError,
    SelfAttackRejected,
    DuplicateAttack,
    DuplicateAchievement,
    UnknownTeamIdentifier,
    InvalidObservation,
    ProducerTimeout,
    ProducerUnavailable,
    AmbiguousFlagMatch,
    FlagExpired,
    RoundOutOfRange,
    HardeningActive,
    AwdpExploitSucceeded,
    AwdpPatchFailed,
    AwdpPatchTimeout,
    AwdpServiceAbnormal,
    AwdpViolation,
    ForeignTeamFlagDetected,
    InsufficientScore,
    HintUnavailable,
    AwdpPlatformFailed
}
