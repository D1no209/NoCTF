namespace NoCTF.Domain.Challenges;

public sealed class ChallengeFlag
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? StageId { get; set; }
    public Guid? ChallengeInstanceId { get; set; }
    public string Flag { get; set; } = string.Empty;
    public DateTimeOffset? ValidStart { get; set; }
    public DateTimeOffset? ValidEnd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long RowVersion { get; set; }
}
