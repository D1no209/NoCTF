using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.Messaging;

public sealed record PollKohChallenge(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    DateTimeOffset RunningSince,
    DateTimeOffset DueAt);

public sealed record RecordKohObservation(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid? TeamId,
    GameplayFactResult? Result,
    GameplayFactFailureCode? FailureCode,
    DateTimeOffset RunningSince,
    DateTimeOffset DueAt,
    DateTimeOffset ObservedAt);
