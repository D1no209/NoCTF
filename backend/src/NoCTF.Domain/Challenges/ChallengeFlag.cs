using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Challenges;

public enum ChallengeFlagType : short
{
    Template,
    Competition,
    Team,
    AwdRound,
    RuntimeInstance
}

[PersistentHierarchy]
[GeneratePersistentLeaves(typeof(ChallengeFlagType), "ChallengeFlag")]
public abstract class ChallengeFlag : IConcurrencyTracked
{
    protected ChallengeFlag(ChallengeFlagType type) => Type = type;
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public ChallengeFlagType Type { get; private set; }
    public Guid? ChallengeId { get; set; }
    public Guid? CompetitionChallengeId { get; set; }
    public Guid? TeamId { get; set; }
    public byte[] FlagSha256 { get; set; } = [];
    public SpecificationKind? SpecificationKind { get; set; }
    public Guid? SpecificationId { get; set; }
    public string SpecificationIdentity { get; set; } = string.Empty;
    public string Flag { get; set; } = string.Empty;
    public ChallengeFlagMatchKind MatchKind { get; set; }
    public DateTimeOffset? ValidStart { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public enum ChallengeFlagMatchKind : short
{
    Exact,
    RegularExpression
}

public enum SpecificationKind : short
{
    Attachment,
    AwdRound,
    RuntimeDefinition,
    Hint,
    RuntimeInstance
}
