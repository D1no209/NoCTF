using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Challenges.Questions;

/// <summary>A private competition-scoped question raised by one participant.</summary>
public sealed class CompetitionQuestion
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid? CompetitionChallengeId { get; set; }
    public Guid TeamId { get; set; }
    public Guid AskedByUserId { get; set; }
    public Guid? SubmissionId { get; set; }
    public CompetitionQuestionSubject Subject { get; set; }
    [MaxLength(160)]
    public string Title { get; set; } = string.Empty;
    [MaxLength(4000)]
    public string Body { get; set; } = string.Empty;
    public CompetitionQuestionStatus Status { get; set; }
    public int Revision { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<CompetitionQuestionEntry> Entries { get; set; } = [];
}
public enum CompetitionQuestionSubject : short
{
    Challenge,
    Platform
}

public enum CompetitionQuestionStatus : short
{
    Pending,
    Replied,
    Resolved,
    Closed
}
