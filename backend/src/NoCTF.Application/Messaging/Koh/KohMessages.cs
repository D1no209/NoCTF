namespace NoCTF.Application.Messaging;

public sealed record PollKohChallenge(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    long ProcessingVersion,
    DateTimeOffset DueAt);

public sealed record RecordKohObservation(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid? TeamId,
    long ProcessingVersion,
    DateTimeOffset ObservedAt);
