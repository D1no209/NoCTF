using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Challenges.Questions;

/// <summary>A reply or immutable audit fact within a competition question.</summary>
public sealed class CompetitionQuestionEntry
{
    public Guid Id { get; set; }
    public Guid QuestionId { get; set; }
    public CompetitionQuestionEntryKind Kind { get; set; }
    public CompetitionQuestionParticipantRole ActorRole { get; set; }
    public Guid ActorUserId { get; set; }
    [MaxLength(4000)]
    public string? Body { get; set; }
    public CompetitionQuestionStatus? FromStatus { get; set; }
    public CompetitionQuestionStatus? ToStatus { get; set; }
    public Guid? TargetEntryId { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? PublishedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public enum CompetitionQuestionEntryKind : short
{
    Message,
    StatusTransition,
    Publication
}

public enum CompetitionQuestionParticipantRole : short
{
    Asker,
    Handler
}
