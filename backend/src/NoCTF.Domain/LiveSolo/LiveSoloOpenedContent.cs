using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.LiveSolo;

/// <summary>Material actually disclosed in a Round. These references survive later template edits.</summary>
public sealed class LiveSoloOpenedContent
{
    [Key] public Guid RoundQuestionId { get; set; }
    [MaxLength(160)] public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    [MaxLength(96)] public string Direction { get; set; } = string.Empty;
    public bool RandomAttachment { get; set; }
    public List<LiveSoloOpenedTag> Tags { get; set; } = [];
    public List<LiveSoloOpenedHint> Hints { get; set; } = [];
    public List<LiveSoloOpenedAttachment> Attachments { get; set; } = [];
}

public sealed class LiveSoloOpenedTag
{
    public Guid RoundQuestionId { get; set; }
    public int Position { get; set; }
    [MaxLength(40)] public string Name { get; set; } = string.Empty;
}

public sealed class LiveSoloOpenedHint
{
    public Guid RoundQuestionId { get; set; }
    public Guid HintId { get; set; }
    public int Position { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; }
}

public sealed class LiveSoloOpenedAttachment
{
    public Guid RoundQuestionId { get; set; }
    public Guid AttachmentId { get; set; }
    public Guid FileId { get; set; }
    public int Position { get; set; }
}
