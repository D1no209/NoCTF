using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Domain.Questionnaires.Activities;

public enum QuestionnaireActivityKind : short { Practice, Examination }
public enum QuestionnaireActivityState : short { Draft, Published, Finished, Voided }
public enum QuestionnaireScoringMode : short { Highest, Latest, Cumulative }
public enum QuestionnaireSelectionKind : short { Draw, Fixed }
public enum QuestionnaireActivityOperationKind : short
{
    Published, Withdrawn, Paused, Resumed, DeadlineExtended, IssuanceStopped, Finished,
    Voided, ExposureAccepted, SessionVoided, AnswerCorrected, FullCreditGranted
}

public sealed class QuestionnaireActivity : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public QuestionnaireActivityKind Kind { get; set; }
    public QuestionnaireActivityState State { get; set; }
    [Required, MaxLength(160)] public string Name { get; set; } = string.Empty;
    public DateTimeOffset OpensAt { get; set; }
    public DateTimeOffset ClosesAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int DurationSeconds { get; set; }
    public int? AttemptLimit { get; set; } = 1;
    public QuestionnaireScoringMode ScoringMode { get; set; }
    public bool IssuanceStopped { get; set; }
    public bool AllowExposedQuestions { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<QuestionnaireDrawRule> Rules { get; set; } = [];
    public QuestionnaireSelectionKind SelectionKind { get; set; }
    public List<QuestionnaireFixedSelection> FixedQuestionnaires { get; set; } = [];
    public List<QuestionnaireActivityPause> Pauses { get; set; } = [];
}

public sealed class QuestionnaireFixedSelection
{
    public Guid ActivityId { get; set; }
    public int Position { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid? RevisionId { get; set; }
    public int PointsPerQuestion { get; set; }
}

public sealed class QuestionnaireDrawRule
{
    public Guid Id { get; set; }
    public Guid ActivityId { get; set; }
    public int Order { get; set; }
    public QuestionnaireQuestionKind Kind { get; set; }
    public QuestionnaireDifficulty? Difficulty { get; set; }
    public int Count { get; set; }
    public int Points { get; set; }
    public List<QuestionnaireDrawRuleFilter> Filters { get; set; } = [];
    public List<QuestionnaireDrawCandidate> Candidates { get; set; } = [];
}

public sealed class QuestionnaireDrawRuleFilter
{
    public Guid RuleId { get; set; }
    public QuestionnaireClassificationKind Kind { get; set; }
    [MaxLength(80)] public string NormalizedName { get; set; } = string.Empty;
}

/// <summary>The eligible immutable question versions frozen at publication.</summary>
public sealed class QuestionnaireDrawCandidate
{
    public Guid RuleId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid SourceId { get; set; }
}

public sealed class QuestionnaireActivityPause : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid ActivityId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}

public sealed class QuestionnaireActivityOperation
{
    public Guid Id { get; set; }
    public Guid ActivityId { get; set; }
    public Guid ActorId { get; set; }
    public QuestionnaireActivityOperationKind Kind { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? RevisionId { get; set; }
    [MaxLength(2000)] public string? Reason { get; set; }
}

public sealed class QuestionnaireQuestionExposure
{
    public Guid SourceId { get; set; }
    public Guid ActivityId { get; set; }
    public DateTimeOffset FirstExposedAt { get; set; }
}
