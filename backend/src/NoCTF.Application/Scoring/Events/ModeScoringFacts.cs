using NoCTF.Application.Submissions.Events;

namespace NoCTF.Application.Scoring.Events;

public sealed record AwdAttackRewarded(
    Guid CompetitionId,
    Guid AttackerTeamId,
    Guid VictimTeamId,
    Guid ChallengeId,
    int Round,
    DateTimeOffset ScoredAt,
    Guid SourceSubmissionId) : IScoringStreamEvent;

public sealed record AwdVictimPenaltyApplied(
    Guid CompetitionId,
    Guid VictimTeamId,
    Guid ChallengeId,
    int Round,
    DateTimeOffset ScoredAt,
    Guid SourceSubmissionId) : IScoringStreamEvent;

public sealed record AwdpBreakAchieved(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    int Round,
    DateTimeOffset ScoredAt,
    Guid SourceSubmissionId) : IScoringStreamEvent;

public sealed record AwdpFixAchieved(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    int Round,
    DateTimeOffset ScoredAt,
    Guid SourceSubmissionId) : IScoringStreamEvent;

public sealed record KohControlIntervalScored(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    DateTimeOffset ScoredAt,
    Guid SourceInputId) : IScoringStreamEvent;

public sealed record PenetrationStageScored(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    Guid StageId,
    DateTimeOffset ScoredAt,
    Guid SourceInputId) : IScoringStreamEvent;
