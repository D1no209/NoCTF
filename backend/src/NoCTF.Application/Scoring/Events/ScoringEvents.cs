using NoCTF.Application.Submissions.Events;

namespace NoCTF.Application.Scoring.Events;

public sealed record ScoreAwarded(
    Guid CompetitionId,
    Guid TeamId,
    Guid? ChallengeId,
    long Points,
    string Reason,
    DateTimeOffset ScoredAt,
    Guid SourceSubmissionId) : IScoringStreamEvent;

public sealed record ScoreDeducted(
    Guid CompetitionId,
    Guid TeamId,
    Guid? ChallengeId,
    long Points,
    string Reason,
    DateTimeOffset ScoredAt,
    Guid SourceSubmissionId) : IScoringStreamEvent;

public sealed record SolveRecorded(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    DateTimeOffset SolvedAt,
    Guid SourceSubmissionId) : IScoringStreamEvent;
