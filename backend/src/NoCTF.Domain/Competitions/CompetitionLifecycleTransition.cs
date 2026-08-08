namespace NoCTF.Domain.Competitions;

/// <summary>In-memory projection of a lifecycle transition carried by CompetitionEvent.</summary>
public sealed class CompetitionLifecycleTransition
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public CompetitionStatus From { get; set; }
    public CompetitionStatus To { get; set; }
    public Guid? ActorId { get; set; }
    public string? Reason { get; set; }
    public bool Automatic { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
