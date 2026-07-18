using NoCTF.Application.Submissions.Events;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Submissions.Processing;

public sealed record SubmissionHistoryItem(
    long Sequence,
    ISubmissionStreamEvent Event,
    DateTimeOffset Timestamp);

public sealed record AwdFlagEvidence(
    Guid TeamId,
    Guid ChallengeId,
    string Flag,
    int Round,
    DateTimeOffset OccurredAt);

public enum ArchiveValidationStatus
{
    Valid,
    Missing,
    LengthMismatch,
    ContentTypeMismatch,
    HashMismatch,
    TeamFailure,
    PlatformFailed
}

public sealed record FixArchiveEvidence(
    ArchiveValidationStatus Status,
    SubmissionErrorCode? ErrorCode = null);

public sealed record SubmissionEvaluationContext(
    Guid CompetitionId,
    GameMode Mode,
    DateTimeOffset CompetitionStart,
    int RoundDurationSeconds,
    string CompetitionConfigurationJson,
    string ChallengeConfigurationJson,
    IReadOnlyList<SubmissionHistoryItem> History,
    long CurrentSubmissionSequence,
    string? ExpectedFlag,
    IReadOnlyList<AwdFlagEvidence> AwdFlags,
    FixArchiveEvidence? Archive);

public sealed record SubmissionEvaluationResult(
    SubmissionOutcome Outcome,
    bool ConsumedAttempt,
    SubmissionErrorCode? ErrorCode = null,
    int? OriginalRound = null,
    int? Round = null);

public interface IGameModeSubmissionEvaluator
{
    GameMode Mode { get; }
}

public interface IGameModeFlagSubmissionEvaluator : IGameModeSubmissionEvaluator
{
    SubmissionEvaluationResult EvaluateFlag(
        FlagSubmissionReceived submission,
        SubmissionEvaluationContext context);
}

public interface IGameModeFixSubmissionEvaluator : IGameModeSubmissionEvaluator
{
    SubmissionEvaluationResult EvaluateFix(
        FixSubmissionReceived submission,
        SubmissionEvaluationContext context);
}
