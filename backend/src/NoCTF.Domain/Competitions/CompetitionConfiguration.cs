namespace NoCTF.Domain.Competitions;

/// <summary>Stores an immutable mode identity and its versioned JSON configuration.</summary>
public sealed class CompetitionConfiguration
{
    public Guid CompetitionId { get; set; }
    public GameMode Mode { get; set; }
    public string Json { get; set; } = """{"schemaVersion":1}""";
    public int Revision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
