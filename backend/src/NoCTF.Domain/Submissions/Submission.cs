namespace NoCTF.Domain.Submissions;

/// <summary>Accepted input facts. Processing conclusions live in <see cref="ScoringEvent"/>.</summary>
public sealed class Submission
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? ChallengeId { get; set; }
    public Guid? UserId { get; set; }
    public SubmissionKind Kind { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string? Flag { get; set; }
    public string? IdempotencyKey { get; set; }
    public Guid? SubjectTeamId { get; set; }
    public Guid? VictimTeamId { get; set; }
    public Guid? ServiceId { get; set; }
    public Guid? StageId { get; set; }
    public long? ControlIntervalSeconds { get; set; }
    public bool CheckerPlatformError { get; set; }
    public Guid? ScoringEventId { get; set; }
    public ScoringEvent? ScoringEvent { get; set; }
    public long ProcessingVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
