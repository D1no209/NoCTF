namespace NoCTF.Domain.Challenges;

/// <summary>Represents one competition-scoped use of a reusable challenge template.</summary>
public sealed class CompetitionChallenge
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ChallengeId { get; set; }
    public long BaseScore { get; set; }
    public int Order { get; set; }
    public bool IsPublished { get; set; }
    public string RulesJson { get; set; } = """{"schemaVersion":1}""";
    public int Revision { get; set; }
    public int LastScheduledAwdRound { get; set; }
    public int AwdScheduleCompetitionRevision { get; set; }
    public long AwdScheduleChallengeRevision { get; set; }
    public DateTimeOffset? AwdScheduleDueAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public List<CompetitionChallengeHint> Hints { get; set; } = [];
}

public sealed class CompetitionChallengeHint
{
    public Guid Id { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public string Content { get; set; } = string.Empty;
    public long Cost { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int PublicationRevision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
