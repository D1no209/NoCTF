namespace NoCTF.Application.Submissions.Events;

public sealed record KohControlObserved(
    Guid CompetitionId,
    Guid ChallengeId,
    Guid? ControllerTeamId,
    string? ControllerName,
    bool IsAuthoritative,
    DateTimeOffset ObservedAt) : ISubmissionStreamEvent;

public sealed record SystemScoringInput(
    Guid CompetitionId,
    Guid TeamId,
    string Kind,
    long Points,
    DateTimeOffset OccurredAt,
    string IdempotencyKey) : ISubmissionStreamEvent;
