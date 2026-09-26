using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Storage;

namespace NoCTF.Domain.Competitions.Progression;

public sealed class CompetitionProgression : IConcurrencyTracked
{
    public Guid CompetitionId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public long Revision { get; set; }
    public bool Enabled { get; set; }
    public bool ShowPlayerMap { get; set; }
    public List<ProgressionNode> Nodes { get; set; } = [];
    public List<ProgressionEdge> Edges { get; set; } = [];
}

public enum ProgressionNodeKind : short
{
    Challenge,
    Badge
}

[PersistentHierarchy]
public abstract class ProgressionNode
{
    protected ProgressionNode(ProgressionNodeKind kind) => Kind = kind;

    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public ProgressionNodeKind Kind { get; private set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
}

[PersistentDiscriminator("challenge")]
public sealed class ChallengeProgressionNode() : ProgressionNode(ProgressionNodeKind.Challenge)
{
    public Guid CompetitionChallengeId { get; set; }
}

[PersistentDiscriminator("badge")]
public sealed class BadgeProgressionNode() : ProgressionNode(ProgressionNodeKind.Badge)
{
    public Guid CompetitionBadgeId { get; set; }
}

public enum ProgressionPrerequisiteCondition : short
{
    Completed,
    Incomplete
}

public sealed class ProgressionEdge
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid SourceNodeId { get; set; }
    public Guid TargetNodeId { get; set; }
    public ProgressionPrerequisiteCondition Condition { get; set; }
}

public sealed class CompetitionBadge : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(1000)]
    public string? Description { get; set; }
    public Guid ImageFileId { get; set; }
    public StoredFile? ImageFile { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class TeamProgressionNodeState : IConcurrencyTracked
{
    public Guid TeamId { get; set; }
    public Guid NodeId { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public long GraphRevision { get; set; }
    public bool Active { get; set; }
    public bool Complete { get; set; }
    public DateTimeOffset EvaluatedAt { get; set; }
}

public sealed class TeamProgressionBadgeState : IConcurrencyTracked
{
    public Guid TeamId { get; set; }
    public Guid BadgeId { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public long GraphRevision { get; set; }
    public bool Active { get; set; }
    public DateTimeOffset EvaluatedAt { get; set; }
}

public sealed class UserBadgeGrant : IConcurrencyTracked
{
    public Guid TeamId { get; set; }
    public Guid BadgeId { get; set; }
    public Guid UserId { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public bool Active { get; set; }
    public DateTimeOffset AwardedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public enum BadgeTransitionKind : short
{
    Awarded,
    Revoked
}

public sealed class UserBadgeTransition
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid BadgeId { get; set; }
    public Guid UserId { get; set; }
    public BadgeTransitionKind Kind { get; set; }
    public long GraphRevision { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
