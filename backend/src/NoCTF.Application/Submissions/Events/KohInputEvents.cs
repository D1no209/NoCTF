namespace NoCTF.Application.Submissions.Events;

public sealed record KohControlObserved(
    Guid CompetitionId,
    Guid ChallengeId,
    Guid? ControllerTeamId,
    string? ControllerName,
    bool IsAuthoritative,
    DateTimeOffset ObservedAt,
    string IdempotencyKey);

public sealed record SystemScoringInput(
    Guid CompetitionId,
    Guid TeamId,
    SystemScoreKind Kind,
    long Points,
    DateTimeOffset OccurredAt,
    string IdempotencyKey);
