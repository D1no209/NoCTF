namespace NoCTF.Domain.Challenges;

public enum ChallengeFlagStatus
{
    Active,
    PendingInjection
}

public sealed class ChallengeFlag
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? StageId { get; set; }
    public Guid? ChallengeInstanceId { get; set; }
    public string Flag { get; set; } = string.Empty;
    public DateTimeOffset? ValidStart { get; set; }
    public DateTimeOffset? ValidEnd { get; set; }
    public ChallengeFlagStatus Status { get; set; }
    public Guid? InjectionClaimToken { get; set; }
    public DateTimeOffset? InjectionClaimedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long RowVersion { get; set; }
}
