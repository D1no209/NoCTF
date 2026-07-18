namespace NoCTF.Domain.Challenges;

/// <summary>Represents a publishable hint for a challenge.</summary>
public sealed class ChallengeHint
{
    public Guid Id { get; set; }
    public Guid ChallengeId { get; set; }
    public string Content { get; set; } = string.Empty;
    public int Cost { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
