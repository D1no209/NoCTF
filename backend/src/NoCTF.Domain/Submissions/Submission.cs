using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Submissions;

/// <summary>Accepted input facts. Processing conclusions live in <see cref="ScoringEvent"/>.</summary>
public sealed class Submission
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid SubmittedByUserId { get; set; }
    public SubmissionKind Kind { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string? SubmittedFlag { get; set; }
    public byte[]? SubmittedFlagSha256 { get; set; }
    public Guid? PatchUploadId { get; set; }
    public SubmissionEvaluationState EvaluationState { get; set; }
    public ScoringFailureCode? EvaluationFailureCode { get; set; }
    public DateTimeOffset EvaluationUpdatedAt { get; set; }
    public Guid? CurrentScoringEventId { get; set; }
    public long ProcessingVersion { get; set; }
    [MaxLength(32)]
    public byte[]? EvaluationResultBodySha256 { get; set; }
}
