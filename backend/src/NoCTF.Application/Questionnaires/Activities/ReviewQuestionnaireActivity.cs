using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Domain.Questionnaires.Sessions;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Application.Questionnaires.Sessions;

namespace NoCTF.Application.Questionnaires.Activities;

public sealed record QuestionnaireReviewRow(Guid SessionId, Guid ParticipantId, Guid UserId, string Name,
    int Attempt, QuestionnaireSessionState State, DateTimeOffset OpenedAt, DateTimeOffset? SubmittedAt, long? Score)
{
    public int QuestionCount { get; init; }
    public int AnsweredCount { get; init; }
    public QuestionnaireSubmissionKind? SubmissionKind { get; init; }
    public QuestionnaireDeadline? Deadline { get; init; }
    public QuestionnaireEffectiveScore? EffectiveScore { get; init; }
}
public sealed record QuestionnaireReviewPage(IReadOnlyList<QuestionnaireReviewRow> Items, int Total);
public sealed record QuestionnaireExamRank(int Rank, Guid UserId, string Name, long Points, DateTimeOffset? AchievedAt, bool HasSubmission);
public sealed record QuestionnaireExamLeaderboard(IReadOnlyList<QuestionnaireExamRank> Items, int Total);
public sealed record QuestionnaireReviewQuestion(QuestionnaireParticipantQuestion Question, Guid RevisionId,
    IReadOnlyList<Guid> CorrectOptions, string? Explanation, QuestionnaireAnswerResult? Result);
public sealed record QuestionnaireReviewSession(QuestionnaireReviewRow Session, IReadOnlyList<QuestionnaireReviewQuestion> Questions);
public enum QuestionnaireReviewFailure { NotFoundOrForbidden, InvalidCorrection, PreviewChanged, ReasonRequired, Conflict }
public sealed record QuestionnaireCorrectionInput(Guid RevisionId, QuestionnaireCorrectionMode Mode, IReadOnlyList<Guid> CorrectOptions);
public sealed record QuestionnaireScoreImpact(Guid SessionId, Guid ParticipantId, long Before, long After);
public sealed record QuestionnaireParticipantScoreImpact(Guid ParticipantId, string Name, long Before, long After);
public sealed record QuestionnaireCorrectionPreview(Guid Token, DateTimeOffset ExpiresAt, int AffectedSessions,
    IReadOnlyList<QuestionnaireScoreImpact> Scores)
{
    public IReadOnlyList<QuestionnaireParticipantScoreImpact> Participants { get; init; } = [];
}
public sealed record QuestionnaireCorrectionResult(QuestionnaireCorrectionPreview? Preview = null, Guid? TargetRevision = null, QuestionnaireReviewFailure? Failure = null);
public sealed record QuestionnaireAuditRow(Guid Id, QuestionnaireActivityOperationKind Kind, string ActorName, DateTimeOffset OccurredAt, string? Reason);
public sealed record QuestionnaireAuditPage(IReadOnlyList<QuestionnaireAuditRow> Items, int Total);
public interface IQuestionnaireReviewStore
{
    Task<QuestionnaireAuditPage?> AuditAsync(Guid actorId, Guid activityId, int offset, int limit, DateTimeOffset now, CancellationToken ct);
    Task<QuestionnaireExamLeaderboard?> LeaderboardAsync(Guid actorId, Guid activityId, int offset, int limit, DateTimeOffset now, CancellationToken ct);
    Task<QuestionnaireReviewPage?> ListAsync(Guid actorId, Guid activityId, int offset, int limit, DateTimeOffset now, CancellationToken ct);
    Task<QuestionnaireReviewSession?> ReadAsync(Guid actorId, Guid activityId, Guid sessionId, DateTimeOffset now, CancellationToken ct);
    Task<QuestionnaireCorrectionResult> PreviewAsync(Guid actorId, Guid activityId, QuestionnaireCorrectionInput input, DateTimeOffset now, CancellationToken ct);
    Task<QuestionnaireCorrectionResult> CorrectAsync(Guid actorId, Guid activityId, QuestionnaireCorrectionInput input, Guid token, DateTimeOffset expiresAt,
        string reason, DateTimeOffset now, CancellationToken ct);
    Task<QuestionnaireReviewFailure?> VoidSessionAsync(Guid actorId, Guid activityId, Guid sessionId, string reason, DateTimeOffset now, CancellationToken ct);
    Task ApplyAsync(ApplyQuestionnaireCorrection message, DateTimeOffset now, CancellationToken ct);
}
public sealed record ApplyQuestionnaireCorrection(Guid ActivityId, Guid RevisionId, Guid TargetRevision);
