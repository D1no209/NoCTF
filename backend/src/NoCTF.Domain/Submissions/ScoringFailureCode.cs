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
    DuplicateAchievement,
    UnknownTeamIdentifier,
    InvalidObservation,
    ProducerTimeout,
    ProducerUnavailable,
    FlagExpired,
    RoundOutOfRange,
    AwdpFixFailed,
    AwdpPatchFailed,
    AwdpPatchTimeout,
    AwdpServiceDown,
    AwdpViolation
}
