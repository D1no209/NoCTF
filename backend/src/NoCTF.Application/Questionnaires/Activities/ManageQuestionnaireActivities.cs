using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Application.Questionnaires.Sessions;

namespace NoCTF.Application.Questionnaires.Activities;

public enum QuestionnaireActivityError { NotFoundOrForbidden, InvalidConfiguration, PublishedConfigurationLocked,
    InsufficientQuestions, ExposedQuestions, Conflict, InvalidTransition, SessionsAlreadyIssued, DeadlineExpired }
public enum QuestionnaireActivityAction { Publish, Withdraw, Pause, Resume, StopIssuance, ExtendDeadline, Finish, Void }
public sealed record QuestionnaireRuleInput(QuestionnaireQuestionKind Kind, QuestionnaireDifficulty? Difficulty, int Count, int Points,
    IReadOnlyList<QuestionnaireClassificationFilter> Filters);
public sealed record QuestionnaireClassificationFilter(QuestionnaireClassificationKind Kind, string Name);
public sealed record QuestionnaireActivityInput(string Name, QuestionnaireActivityKind Kind, DateTimeOffset OpensAt, DateTimeOffset ClosesAt,
    int DurationSeconds, int? AttemptLimit, QuestionnaireScoringMode ScoringMode, bool AllowExposedQuestions, IReadOnlyList<QuestionnaireRuleInput> Rules)
{
    public QuestionnaireSelectionKind SelectionKind { get; init; }
    public IReadOnlyList<QuestionnaireFixedSelectionInput> FixedQuestionnaires { get; init; } = [];
}
public sealed record QuestionnaireFixedSelectionInput(Guid ChallengeId, int PointsPerQuestion);
public sealed record QuestionnaireActivityCommand(Guid ActorId, Guid? ActivityId, QuestionnaireActivityInput Input, DateTimeOffset Now);
public sealed record QuestionnaireActivityTransition(Guid ActorId, Guid ActivityId, QuestionnaireActivityAction Action,
    DateTimeOffset Now, DateTimeOffset? Deadline = null, string? Reason = null);
public sealed record QuestionnaireActivitySummary(Guid Id, string Name, QuestionnaireActivityKind Kind,
    QuestionnaireActivityState State, DateTimeOffset OpensAt, DateTimeOffset ClosesAt, int DurationSeconds,
    int? AttemptLimit, QuestionnaireScoringMode ScoringMode, long MaximumSessionPoints, bool IssuanceStopped)
{
    public int QuestionCount { get; init; }
}
public sealed record QuestionnaireActivityPage(IReadOnlyList<QuestionnaireActivitySummary> Items, int Total);
public sealed record QuestionnaireActivityManagement(QuestionnaireActivitySummary Activity, IReadOnlyList<QuestionnaireRuleInput> Rules,
    bool AllowExposedQuestions, bool CanManage, bool CanJudge, QuestionnaireDeadline Deadline)
{
    public QuestionnaireSelectionKind SelectionKind { get; init; }
    public IReadOnlyList<QuestionnaireFixedSelectionInput> FixedQuestionnaires { get; init; } = [];
    public IReadOnlyList<QuestionnaireCorrectionProgress> Corrections { get; init; } = [];
    public int IssuedCount { get; init; }
    public IReadOnlyList<QuestionnaireActivityAction> AllowedOperations { get; init; } = [];
}
public sealed record QuestionnaireCorrectionProgress(Guid RevisionId, Guid TargetRevision, bool Completed);
public sealed record QuestionnaireRuleCapacity(int Order, int Required, int Available);
public sealed record QuestionnaireActivityCheck(bool Feasible, IReadOnlyList<QuestionnaireRuleCapacity> Rules, IReadOnlyList<QuestionnaireAllocationShortage> Shortages);
public sealed record QuestionnaireActivityResult(QuestionnaireActivitySummary? Activity = null, QuestionnaireActivityError? Failure = null,
    QuestionnaireActivityFailure? Validation = null, IReadOnlyList<QuestionnaireAllocationShortage>? Shortages = null);
public interface IQuestionnaireActivityStore
{
    Task<QuestionnaireActivityResult> SaveAsync(QuestionnaireActivityCommand command, CancellationToken ct);
    Task<QuestionnaireActivityResult> TransitionAsync(QuestionnaireActivityTransition command, CancellationToken ct);
    Task<QuestionnaireActivityPage> ListAsync(Guid actorId, bool staff, CancellationToken ct, int offset = 0, int limit = 25, string? keyword = null);
    Task<QuestionnaireActivityManagement?> ReadManagementAsync(Guid actorId, Guid activityId, DateTimeOffset now, CancellationToken ct);
    Task<QuestionnaireActivityCheck?> CheckAsync(Guid actorId, Guid activityId, DateTimeOffset now, CancellationToken ct);
}

public static class QuestionnaireActivityMapping
{
    public static QuestionnaireActivity Build(QuestionnaireActivityCommand command) => new()
    {
        Id = command.ActivityId ?? Guid.CreateVersion7(command.Now), OwnerId = command.ActorId, CreatedAt = command.Now,
        Name = command.Input.Name.Trim(), Kind = command.Input.Kind, OpensAt = command.Input.OpensAt,
        ClosesAt = command.Input.ClosesAt, DurationSeconds = command.Input.DurationSeconds,
        AttemptLimit = command.Input.AttemptLimit, ScoringMode = command.Input.ScoringMode,
        AllowExposedQuestions = command.Input.AllowExposedQuestions, SelectionKind = command.Input.SelectionKind,
        FixedQuestionnaires = command.Input.FixedQuestionnaires.Select((value, position) => new QuestionnaireFixedSelection
            { Position = position, ChallengeId = value.ChallengeId, PointsPerQuestion = value.PointsPerQuestion }).ToList(),
        Rules = command.Input.Rules.Select((rule, index) => new QuestionnaireDrawRule
        {
            Id = Guid.NewGuid(), Order = index, Kind = rule.Kind, Difficulty = rule.Difficulty, Count = rule.Count, Points = rule.Points,
            Filters = rule.Filters.Select(filter => new QuestionnaireDrawRuleFilter
            { Kind = filter.Kind, NormalizedName = filter.Name.Trim().ToUpperInvariant() }).ToList()
        }).ToList()
    };

    public static QuestionnaireActivitySummary Summary(QuestionnaireActivity activity) => new(activity.Id,
        activity.Name, activity.Kind, activity.State, activity.OpensAt, activity.ClosesAt, activity.DurationSeconds,
        activity.AttemptLimit, activity.ScoringMode, activity.Rules.Sum(rule => (long)rule.Count * rule.Points), activity.IssuanceStopped)
    { QuestionCount = activity.Rules.Sum(rule => rule.Count) };
}
