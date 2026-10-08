using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Challenges;

/// <summary>Represents one competition-scoped use of a reusable challenge template.</summary>
[PersistentHierarchy]
[GeneratePersistentLeaves(typeof(GameMode), "CompetitionChallenge")]
public abstract class CompetitionChallenge : IConcurrencyTracked
{
    protected CompetitionChallenge(GameMode mode) => Mode = mode;
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid CompetitionId { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid? DirectionId { get; set; }
    public NoCTF.Domain.Competitions.Directions.CompetitionDirection? Direction { get; set; }
    public GameMode Mode { get; private set; }
    [MaxLength(160)]
    public string? CustomTitle { get; set; }
    [MaxLength(160)]
    public string? NormalizedCustomTitle { get; set; }
    public int Order { get; set; }
    public bool IsPublished { get; set; }
    public int? WriteUpDeductionPercent { get; set; }
    public CompetitionChallengeRules? Rules { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public List<CompetitionChallengeHint> Hints { get; set; } = [];
    public List<CompetitionChallengeTag> Tags { get; set; } = [];
}

public sealed class CompetitionChallengeHint
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public long Cost { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? HiddenAt { get; set; }
}
