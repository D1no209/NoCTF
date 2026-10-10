using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.LiveSolo;

public sealed class LiveSoloQuestionGroup : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    [MaxLength(160)] public string Name { get; set; } = string.Empty;
    public int Position { get; set; }
    public bool Reserve { get; set; }
    public int? RoundLimitSeconds { get; set; }
    public List<LiveSoloQuestionGroupItem> Items { get; set; } = [];
}

public sealed class LiveSoloQuestionGroupItem
{
    public Guid QuestionGroupId { get; set; }
    public int Position { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public int? OpenOffsetSeconds { get; set; }
}

/// <summary>Opaque provenance survives deletion of the original template without coupling copied definitions.</summary>
public sealed class LiveSoloChallengeSource
{
    [Key] public Guid ChallengeId { get; set; }
    public Guid OriginalChallengeId { get; set; }
    public Guid CanonicalChallengeId { get; set; }
    public DateTimeOffset CopiedAt { get; set; }
}

public sealed class LiveSoloQuestionExposure
{
    public Guid CompetitionId { get; set; }
    public Guid CanonicalChallengeId { get; set; }
    public DateTimeOffset PublicAt { get; set; }
    public Guid MatchId { get; set; }
}
