using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Challenges.Capabilities;

public enum QuestionnaireQuestionKind : short { SingleChoice, MultipleChoice, TrueFalse }
public enum QuestionnaireDifficulty : short { Basic, Intermediate, Advanced }
public enum QuestionnaireClassificationKind : short { Knowledge, Tag }

[PersistentDiscriminator("questionnaire")]
public sealed class QuestionnaireCapabilityDefinition() : ChallengeCapabilityDefinition(ChallengeCapabilityKind.Questionnaire)
{
    public Guid CurrentRevisionId { get; set; }
    public QuestionnaireRevision CurrentRevision { get; set; } = null!;
}

public sealed class QuestionnaireRevision
{
    public Guid Id { get; set; }
    public Guid ChallengeId { get; set; }
    public int Number { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<QuestionnaireQuestion> Questions { get; set; } = [];
}

public sealed class QuestionnaireQuestion
{
    public Guid VersionId { get; set; } = Guid.NewGuid();
    public Guid RevisionId { get; set; }
    public QuestionnaireRevision Revision { get; set; } = null!;
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public int Position { get; set; }
    public QuestionnaireQuestionKind Kind { get; set; }
    public QuestionnaireDifficulty Difficulty { get; set; }
    [Required, MaxLength(160)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(64000)] public string Body { get; set; } = string.Empty;
    [MaxLength(64000)] public string? Explanation { get; set; }
    public List<QuestionnaireOption> Options { get; set; } = [];
    public List<QuestionnaireClassification> Classifications { get; set; } = [];
    public List<QuestionnaireFile> Files { get; set; } = [];
}

public sealed class QuestionnaireOption
{
    public Guid QuestionVersionId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid QuestionId { get; set; }
    public Guid Id { get; set; }
    public int Position { get; set; }
    [Required, MaxLength(16000)] public string Content { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public bool? TruthValue { get; set; }
}

public sealed class QuestionnaireClassification
{
    public Guid RevisionId { get; set; }
    public Guid QuestionId { get; set; }
    public QuestionnaireClassificationKind Kind { get; set; }
    [MaxLength(80)] public string Name { get; set; } = string.Empty;
    [MaxLength(80)] public string NormalizedName { get; set; } = string.Empty;
}

public sealed class QuestionnaireFile
{
    public Guid RevisionId { get; set; }
    public Guid QuestionId { get; set; }
    public Guid FileId { get; set; }
}
