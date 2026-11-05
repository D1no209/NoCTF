using NoCTF.Application.Questionnaires.Activities;
using NoCTF.Domain.Questionnaires.Sessions;

namespace NoCTF.Application.Questionnaires.Sessions;

public sealed record QuestionnaireAnswerCommand(Guid ActorId, Guid ActivityId, Guid SessionId, Guid ItemId, Guid ExpectedRevision,
    Guid RequestId, IReadOnlyList<Guid> OptionIds, DateTimeOffset Now);
public sealed record QuestionnaireSessionCommand(Guid ActorId, Guid ActivityId, Guid? SessionId, Guid? ExpectedRevision, DateTimeOffset Now,
    Guid? RequestId = null, int ExpectedIssuedCount = 0);
public sealed record QuestionnaireSessionHistory(Guid SessionId, int Attempt, QuestionnaireSessionState State, long? Score, DateTimeOffset? SubmittedAt);
public sealed record QuestionnaireSessionView(QuestionnaireActivitySummary Activity, QuestionnaireDeadline Deadline, QuestionnaireParticipantSession? Session,
    int IssuedCount, QuestionnaireEffectiveScore EffectiveScore, IReadOnlyList<QuestionnaireSessionHistory> History)
{
    public DateTimeOffset ServerTime { get; init; }
    public bool CanIssue { get; init; }
    public bool CanAnswer { get; init; }
}
public sealed record QuestionnaireSessionResult(QuestionnaireSessionView? View = null, QuestionnaireSessionFailure? Failure = null,
    IReadOnlyList<QuestionnaireAllocationShortage>? Shortages = null);
public interface IQuestionnaireSessionStore
{
    Task<QuestionnaireSessionResult> ReadAsync(QuestionnaireSessionCommand command, CancellationToken ct);
    Task<QuestionnaireSessionResult> IssueAsync(QuestionnaireSessionCommand command, CancellationToken ct);
    Task<QuestionnaireSessionResult> AnswerAsync(QuestionnaireAnswerCommand command, CancellationToken ct);
    Task<QuestionnaireSessionResult> SubmitAsync(QuestionnaireSessionCommand command, CancellationToken ct);
    Task SweepAsync(Guid activityId, DateTimeOffset now, CancellationToken ct);
}

public sealed record SweepQuestionnaireActivity(Guid ActivityId);
