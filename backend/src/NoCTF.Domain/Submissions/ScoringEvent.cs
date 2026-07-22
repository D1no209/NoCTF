namespace NoCTF.Domain.Submissions;

/// <summary>A score-free, immutable processing or system fact. Soft deletion retires replaced facts.</summary>
public sealed class ScoringEvent
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? CompetitionChallengeId { get; set; }
    public Guid? StageId { get; set; }
    public Guid? ChallengeInstanceId { get; set; }
    public Guid? SubmissionId { get; set; }
    public Submission? Submission { get; set; }
    public ScoringEventKind Kind { get; set; }
    public ScoringResult Result { get; set; }
    public ScoringFailureCode? FailureCode { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
    public string? ProcessedWorkerId { get; set; }
    public string EvaluatorVersion { get; set; } = string.Empty;
    public string? SourceKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }
    public long RowVersion { get; set; }
}
