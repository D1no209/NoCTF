using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Challenges;

/// <summary>Represents one competition-scoped use of a reusable challenge template.</summary>
public sealed class CompetitionChallenge
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ChallengeId { get; set; }
    public int BaseScore { get; set; }
    public int Order { get; set; }
    public bool IsPublished { get; set; }
    public string ConfigurationJson { get; set; } = """{"schemaVersion":1}""";
    public int Revision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public SoftDeleteState Deletion { get; set; } = new();
    public List<CompetitionChallengeHint> Hints { get; set; } = [];
}

public sealed class CompetitionChallengeHint
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public int Cost { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
