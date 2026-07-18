using NoCTF.Application.Submissions.Events;

namespace NoCTF.Application.Scoring.Events;

public enum ScoringReason
{
    Solve,
    Blood,
    AwdServiceUp,
    AwdServiceDown,
    AwdAttack,
    AwdCompromised,
    AwdpBreak,
    AwdpFix,
    KohControlInterval,
    PenetrationStage
}

public sealed record ScoreAwarded(
    Guid CompetitionId,
    Guid TeamId,
    Guid? ChallengeId,
    long Points,
    ScoringReason Reason,
    DateTimeOffset ScoredAt,
    Guid SourceSubmissionId) : IScoringStreamEvent;

public sealed record ScoreDeducted(
    Guid CompetitionId,
    Guid TeamId,
    Guid? ChallengeId,
    long Points,
    ScoringReason Reason,
    DateTimeOffset ScoredAt,
    Guid SourceSubmissionId) : IScoringStreamEvent;

public sealed record CtfSolveRecorded(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    DateTimeOffset SolvedAt,
    Guid SourceSubmissionId) : IScoringStreamEvent;
