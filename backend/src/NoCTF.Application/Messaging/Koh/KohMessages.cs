using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Messaging;

public sealed record PollKohChallenge(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    int CompetitionConfigurationRevision,
    int CompetitionChallengeRevision,
    DateTimeOffset RunningSince,
    DateTimeOffset DueAt);

public sealed record RecordKohObservation(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid? TeamId,
    ScoringResult Result,
    ScoringFailureCode? FailureCode,
    int CompetitionConfigurationRevision,
    int CompetitionChallengeRevision,
    DateTimeOffset RunningSince,
    DateTimeOffset DueAt,
    DateTimeOffset ObservedAt);
