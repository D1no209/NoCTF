using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Theory.Questions;

namespace NoCTF.Domain.Theory.Activities;

public enum TheoryActivityKind : short { Practice, Examination, Competition }
public enum TheoryActivityState : short { Draft, Published, Finished, Voided }
public enum TheoryScoringMode : short { Highest, Latest, Cumulative }
public enum TheoryActivityOperationKind : short
{
    Published, Withdrawn, Paused, Resumed, DeadlineExtended, IssuanceStopped, Finished,
    Voided, ExposureAccepted, PaperVoided, AnswerCorrected, FullCreditGranted
}

public sealed class TheoryActivity : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public TheoryActivityKind Kind { get; set; }
    public TheoryActivityState State { get; set; }
    [Required, MaxLength(160)] public string Name { get; set; } = string.Empty;
    public DateTimeOffset OpensAt { get; set; }
    public DateTimeOffset ClosesAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int DurationSeconds { get; set; }
    public int? AttemptLimit { get; set; } = 1;
    public TheoryScoringMode ScoringMode { get; set; }
    public bool IssuanceStopped { get; set; }
    public bool AllowExposedQuestions { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<TheoryDrawRule> Rules { get; set; } = [];
    public List<TheoryActivityPause> Pauses { get; set; } = [];
}

/// <summary>A real association, without coupling theory questions to practical challenges.</summary>
public sealed class CompetitionTheoryActivity
{
    public Guid CompetitionId { get; set; }
    public Guid ActivityId { get; set; }
    public bool Enabled { get; set; }
}

public sealed class TheoryDrawRule
{
    public Guid Id { get; set; }
    public Guid ActivityId { get; set; }
    public int Order { get; set; }
    public TheoryQuestionKind Kind { get; set; }
    public TheoryQuestionDifficulty? Difficulty { get; set; }
    public int Count { get; set; }
    public int Points { get; set; }
    public List<TheoryDrawRuleFilter> Filters { get; set; } = [];
    public List<TheoryDrawCandidate> Candidates { get; set; } = [];
}

public sealed class TheoryDrawRuleFilter
{
    public Guid RuleId { get; set; }
    public TheoryClassificationKind Kind { get; set; }
    [MaxLength(80)] public string NormalizedName { get; set; } = string.Empty;
}

/// <summary>The eligible immutable question versions frozen at publication.</summary>
public sealed class TheoryDrawCandidate
{
    public Guid RuleId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid SourceId { get; set; }
}

public sealed class TheoryActivityPause : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid ActivityId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}

public sealed class TheoryActivityOperation
{
    public Guid Id { get; set; }
    public Guid ActivityId { get; set; }
    public Guid ActorId { get; set; }
    public TheoryActivityOperationKind Kind { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? PaperId { get; set; }
    public Guid? RevisionId { get; set; }
    [MaxLength(2000)] public string? Reason { get; set; }
}

public sealed class TheoryQuestionExposure
{
    public Guid SourceId { get; set; }
    public Guid ActivityId { get; set; }
    public DateTimeOffset FirstExposedAt { get; set; }
}
