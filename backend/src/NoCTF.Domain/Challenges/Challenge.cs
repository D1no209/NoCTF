using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Challenges;

/// <summary>Represents the mode-independent fields of a challenge.</summary>
public sealed class Challenge
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid[] ManagerIds { get; set; } = [];
    public ChallengeVisibility Visibility { get; set; }
    [MaxLength(160)]
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Direction { get; set; } = "Uncategorized";
    public int Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public List<ChallengeAttachment> Attachments { get; set; } = [];
}

public enum ChallengeVisibility : short
{
    Private,
    Shared
}
