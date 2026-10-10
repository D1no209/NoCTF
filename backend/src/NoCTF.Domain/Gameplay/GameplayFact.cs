using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Gameplay;

/// <summary>A player, administrator, or system fact together with its current authoritative result.</summary>
[PersistentHierarchy]
[GeneratePersistentLeaves(typeof(GameplayFactKind), "GameplayFact")]
public abstract class GameplayFact : IConcurrencyTracked
{
    protected GameplayFact(GameplayFactKind kind) => Kind = kind;
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? VictimTeamId { get; set; }
    public Guid? ActorUserId { get; set; }
    [System.ComponentModel.DataAnnotations.MaxLength(45), System.Text.Json.Serialization.JsonIgnore]
    public string? SourceIpAddress { get; set; }
    public GameplayFactKind Kind { get; private set; }
    public DateTimeOffset OccurredAt { get; set; }
    public GameplayFactReferenceKind? ReferenceKind { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Value { get; set; }
    [MaxLength(32)]
    public byte[]? ValueSha256 { get; set; }
    public GameplayFactState State { get; set; }
    public GameplayFactResult? Result { get; set; }
    public NoCTF.Domain.Challenges.GameplayFactTimeEligibility TimeEligibility { get; set; }
    public Guid AppliedTimingRevision { get; set; }
    public GameplayFactFailureCode? FailureCode { get; set; }
    public FlagAcquisitionEvidence? AcquisitionEvidence { get; set; }
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
    KohControlObservation,
    AttachmentDownload,
    WriteUpUnlock
}

public enum GameplayFactReferenceKind : short
{
    PatchUpload,
    Hint,
    AwdRound,
    Attachment,
    WriteUpVersion
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
    Uncontrolled,
    RightButDue
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
    ForeignTeamFlagDetected,
    InsufficientScore,
    HintUnavailable,
    AwdpPlatformFailed,
    PatchStillExploitable,
    PatchExecutionFailed,
    PatchServiceAbnormal,
    PatchVerificationPlatformFailed,
    StaticFlagWithoutContainer,
    StaticFlagWithoutAttachment,
    StaticFlagWithoutContainerAndAttachment
}
