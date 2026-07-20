namespace NoCTF.Domain.Submissions;

public enum ScoringFailureCode
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
    AchievementAlreadyCompleted,
    StageRequired,
    StageNotFound,
    StagePrerequisiteIncomplete,
    UnknownTeamIdentifier,
    InvalidObservation,
    ProducerTimeout,
    ProducerUnavailable,
    FlagExpired,
    RoundOutOfRange
}
