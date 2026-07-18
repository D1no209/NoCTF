namespace NoCTF.Infrastructure.Eventing.ProjectionCheckpoints;

public sealed class ScoringProjectionCheckpoint
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ActiveScoringStreamId { get; set; }
    public Guid? StagingScoringStreamId { get; set; }
    public long SubmissionHighWaterMark { get; set; }
    public long ProjectionVersion { get; set; }
    public bool IsDirty { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; }
}
