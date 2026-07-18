namespace NoCTF.Application.Submissions.Events;

public sealed record PenetrationStageCompleted(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    Guid StageId,
    DateTimeOffset OccurredAt,
    string IdempotencyKey) : ISubmissionStreamEvent;
