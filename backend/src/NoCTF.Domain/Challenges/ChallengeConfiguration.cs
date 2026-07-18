namespace NoCTF.Domain.Challenges;

/// <summary>Stores a challenge's strictly versioned mode configuration.</summary>
public sealed class ChallengeConfiguration
{
    public Guid ChallengeId { get; set; }
    public string Json { get; set; } = """{"schemaVersion":1}""";
    public int Revision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
