using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Challenges.WriteUps;

public enum WriteUpSource : short { Team, Official }
public enum WriteUpFormat : short { Markdown, Pdf }
public enum WriteUpVersionState : short { Draft, Submitted, Rejected, Approved }
public enum WriteUpReviewAction : short { Publish, Reject, Withdraw }

/// <summary>The stable author slot; submitted content is stored in immutable versions.</summary>
public sealed class ChallengeWriteUp : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public WriteUpSource Source { get; set; }
    public Guid AuthorScopeId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public int NextVersionNumber { get; set; } = 1;
    public Guid? DraftVersionId { get; set; }
    public Guid? SubmittedVersionId { get; set; }
    public Guid? PublishedVersionId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<ChallengeWriteUpVersion> Versions { get; set; } = [];
}

public sealed class ChallengeWriteUpVersion : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid WriteUpId { get; set; }
    public int Number { get; set; }
    public WriteUpFormat Format { get; set; }
    public WriteUpVersionState State { get; set; }
    [MaxLength(262144)] public string? Markdown { get; set; }
    public Guid? FileId { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    [MaxLength(4000)] public string? ReviewReason { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>One formal, team-shared unlock. Its percentage survives policy and publication changes.</summary>
public sealed class WriteUpUnlockReceipt
{
    [Key] public Guid GameplayFactId { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid TeamId { get; set; }
    public Guid VersionId { get; set; }
    [Range(0, 100)] public int DeductionPercent { get; set; }
    public DateTimeOffset UnlockedAt { get; set; }
}
