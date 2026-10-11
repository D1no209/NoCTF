using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Questions;
using NoCTF.Application.Theory.Papers;

namespace NoCTF.Application.Theory.Activities;

public enum TheoryActivityError { NotFoundOrForbidden, InvalidConfiguration, PublishedConfigurationLocked,
    InsufficientQuestions, ExposedQuestions, Conflict, AlreadyAssociated, InvalidTransition, PapersAlreadyIssued, DeadlineExpired }
public enum TheoryActivityAction { Publish, Withdraw, Pause, Resume, StopIssuance, ExtendDeadline, Finish, Void }
public sealed record TheoryRuleInput(TheoryQuestionKind Kind, TheoryQuestionDifficulty? Difficulty, int Count, int Points,
    IReadOnlyList<TheoryClassificationFilter> Filters);
public sealed record TheoryClassificationFilter(TheoryClassificationKind Kind, string Name);
public sealed record TheoryActivityInput(string Name, TheoryActivityKind Kind, DateTimeOffset OpensAt, DateTimeOffset ClosesAt,
    int DurationSeconds, int? AttemptLimit, TheoryScoringMode ScoringMode, bool AllowExposedQuestions, IReadOnlyList<TheoryRuleInput> Rules);
public sealed record TheoryActivityCommand(Guid ActorId, Guid? ActivityId, Guid? CompetitionId, TheoryActivityInput Input, DateTimeOffset Now);
public sealed record TheoryActivityTransition(Guid ActorId, Guid ActivityId, TheoryActivityAction Action,
    DateTimeOffset Now, DateTimeOffset? Deadline = null, string? Reason = null);
public sealed record TheoryActivitySummary(Guid Id, Guid? CompetitionId, string Name, TheoryActivityKind Kind,
    TheoryActivityState State, DateTimeOffset OpensAt, DateTimeOffset ClosesAt, int DurationSeconds,
    int? AttemptLimit, TheoryScoringMode ScoringMode, long MaximumPaperPoints, bool IssuanceStopped)
{
    public int QuestionCount { get; init; }
}
public sealed record TheoryActivityPage(IReadOnlyList<TheoryActivitySummary> Items, int Total);
public sealed record TheoryActivityManagement(TheoryActivitySummary Activity, IReadOnlyList<TheoryRuleInput> Rules,
    bool AllowExposedQuestions, bool CanManage, bool CanJudge, TheoryDeadline Deadline)
{
    public IReadOnlyList<TheoryCorrectionProgress> Corrections { get; init; } = [];
    public int IssuedCount { get; init; }
    public DateTimeOffset? CompetitionEndAt { get; init; }
    public IReadOnlyList<TheoryActivityAction> AllowedOperations { get; init; } = [];
}
public sealed record TheoryCorrectionProgress(Guid RevisionId, Guid TargetRevision, bool Completed);
public sealed record TheoryRuleCapacity(int Order, int Required, int Available);
public sealed record TheoryActivityCheck(bool Feasible, IReadOnlyList<TheoryRuleCapacity> Rules, IReadOnlyList<TheoryAllocationShortage> Shortages);
public sealed record TheoryActivityResult(TheoryActivitySummary? Activity = null, TheoryActivityError? Failure = null,
    TheoryActivityFailure? Validation = null, IReadOnlyList<TheoryAllocationShortage>? Shortages = null);
public interface ITheoryActivityStore
{
    Task<TheoryActivityResult> SaveAsync(TheoryActivityCommand command, CancellationToken ct);
    Task<TheoryActivityResult> TransitionAsync(TheoryActivityTransition command, CancellationToken ct);
    Task<TheoryActivityPage> ListAsync(Guid actorId, bool staff, Guid? competitionId, CancellationToken ct, int offset = 0, int limit = 25, string? keyword = null);
    Task<TheoryActivityManagement?> ReadManagementAsync(Guid actorId, Guid activityId, DateTimeOffset now, CancellationToken ct);
    Task<TheoryActivityCheck?> CheckAsync(Guid actorId, Guid activityId, DateTimeOffset now, CancellationToken ct);
}

public static class TheoryActivityMapping
{
    public static TheoryActivity Build(TheoryActivityCommand command) => new()
    {
        Id = command.ActivityId ?? Guid.CreateVersion7(command.Now), OwnerId = command.ActorId, CreatedAt = command.Now,
        Name = command.Input.Name.Trim(), Kind = command.Input.Kind, OpensAt = command.Input.OpensAt,
        ClosesAt = command.Input.ClosesAt, DurationSeconds = command.Input.DurationSeconds,
        AttemptLimit = command.Input.AttemptLimit, ScoringMode = command.Input.ScoringMode,
        AllowExposedQuestions = command.Input.AllowExposedQuestions,
        Rules = command.Input.Rules.Select((rule, index) => new TheoryDrawRule
        {
            Id = Guid.NewGuid(), Order = index, Kind = rule.Kind, Difficulty = rule.Difficulty, Count = rule.Count, Points = rule.Points,
            Filters = rule.Filters.Select(filter => new TheoryDrawRuleFilter
            { Kind = filter.Kind, NormalizedName = filter.Name.Trim().ToUpperInvariant() }).ToList()
        }).ToList()
    };

    public static TheoryActivitySummary Summary(TheoryActivity activity, Guid? competitionId) => new(activity.Id, competitionId,
        activity.Name, activity.Kind, activity.State, activity.OpensAt, activity.ClosesAt, activity.DurationSeconds,
        activity.AttemptLimit, activity.ScoringMode, activity.Rules.Sum(rule => (long)rule.Count * rule.Points), activity.IssuanceStopped)
    { QuestionCount = activity.Rules.Sum(rule => rule.Count) };
}
