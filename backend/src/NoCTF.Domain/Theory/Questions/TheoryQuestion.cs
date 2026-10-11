using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Theory.Questions;

public enum TheoryQuestionKind : short { SingleChoice, MultipleChoice, TrueFalse }
public enum TheoryQuestionDifficulty : short { Basic, Intermediate, Advanced }
public enum TheoryQuestionState : short { Draft, Available, Retired }
public enum TheoryQuestionPermission : short { Use, Edit, Manage }
public enum TheoryTruthValue : short { True, False }

/// <summary>Independent of the practical Challenge catalog and its game modes.</summary>
public sealed class TheoryQuestion : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public Guid SourceId { get; set; }
    public TheoryQuestionState State { get; set; }
    public int CurrentVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<TheoryQuestionRevision> Revisions { get; set; } = [];
    public List<TheoryQuestionGrant> Grants { get; set; } = [];
}

/// <summary>Canonical identity shared by copies, including copies of copies.</summary>
public sealed class TheoryQuestionSource
{
    public Guid Id { get; set; }
}

/// <summary>Once stored, question content is immutable; changes create another version.</summary>
public sealed class TheoryQuestionRevision
{
    public Guid Id { get; set; }
    public Guid QuestionId { get; set; }
    public int Version { get; set; }
    public TheoryQuestionKind Kind { get; set; }
    public TheoryQuestionDifficulty Difficulty { get; set; }
    [Required, MaxLength(160)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(64000)] public string Body { get; set; } = string.Empty;
    [MaxLength(64000)] public string? Explanation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<TheoryQuestionOption> Options { get; set; } = [];
    public List<TheoryQuestionClassification> Classifications { get; set; } = [];
    public List<TheoryQuestionFile> Files { get; set; } = [];
}

public sealed class TheoryQuestionOption
{
    public Guid Id { get; set; }
    public Guid RevisionId { get; set; }
    public int Order { get; set; }
    [Required, MaxLength(16000)] public string Content { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public TheoryTruthValue? TruthValue { get; set; }
}

public enum TheoryClassificationKind : short { Knowledge, Tag }

public sealed class TheoryQuestionClassification
{
    public Guid RevisionId { get; set; }
    public TheoryClassificationKind Kind { get; set; }
    [MaxLength(80)] public string Name { get; set; } = string.Empty;
    [MaxLength(80)] public string NormalizedName { get; set; } = string.Empty;
}

public sealed class TheoryQuestionGrant
{
    public Guid QuestionId { get; set; }
    public Guid UserId { get; set; }
    public TheoryQuestionPermission Permission { get; set; }
}

public sealed class TheoryQuestionFile
{
    public Guid RevisionId { get; set; }
    public Guid FileId { get; set; }
}
