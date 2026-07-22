namespace NoCTF.Domain.Runtime;

/// <summary>Records the durable identity and status of a challenge runtime.</summary>
public sealed class ChallengeInstance
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid? TeamId { get; set; }
    public RuntimeProvider Provider { get; set; }
    public string Receipt { get; set; } = string.Empty;
    public RuntimeStatus Status { get; set; }
    public string? EntryUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
