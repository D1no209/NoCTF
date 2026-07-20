namespace NoCTF.Domain.Competitions;

/// <summary>Immutable audit fact for a competition lifecycle transition.</summary>
public sealed class CompetitionLifecycleAudit
{
    public Guid Id { get; set; }
    public CompetitionStatus From { get; set; }
    public CompetitionStatus To { get; set; }
    public Guid? ActorId { get; set; }
    public string? Reason { get; set; }
    public bool Automatic { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
