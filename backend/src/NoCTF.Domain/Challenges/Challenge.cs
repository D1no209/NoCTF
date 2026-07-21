using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Challenges;

/// <summary>Represents the mode-independent fields of a challenge.</summary>
public sealed class Challenge
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Direction { get; set; } = "Uncategorized";
    public int Order { get; set; }
    public bool IsPublished { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public SoftDeleteState Deletion { get; set; } = new();
    public List<ChallengeAttachment> Attachments { get; set; } = [];
}
