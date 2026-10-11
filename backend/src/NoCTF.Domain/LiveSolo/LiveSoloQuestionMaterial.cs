using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.LiveSolo;

public enum LiveSoloMaterialOwnership : short { FollowingQuestionBank, CompetitionOwned }

/// <summary>An explicit competition owner for material outside the reusable question bank.</summary>
public sealed class LiveSoloQuestionMaterial : IConcurrencyTracked
{
    [Key] public Guid CompetitionChallengeId { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid? SourceTemplateId { get; set; }
    public Guid? SourceTemplateStamp { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
