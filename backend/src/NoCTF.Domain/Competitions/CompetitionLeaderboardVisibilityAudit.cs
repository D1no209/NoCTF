namespace NoCTF.Domain.Competitions;

/// <summary>Immutable audit fact for an effective leaderboard visibility transition.</summary>
public sealed class CompetitionLeaderboardVisibilityAudit
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public CompetitionLeaderboardVisibility From { get; set; }
    public CompetitionLeaderboardVisibility To { get; set; }
    public DateTimeOffset? DataCutoffAt { get; set; }
    public Guid? ActorId { get; set; }
    public string? Reason { get; set; }
    public bool Automatic { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
