using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Application.Theory.Papers;

namespace NoCTF.Application.Theory.Activities;

public sealed record TheoryReviewRow(Guid PaperId, Guid ParticipantId, Guid? UserId, Guid? TeamId, string Name,
    int Attempt, TheoryPaperState State, DateTimeOffset OpenedAt, DateTimeOffset? SubmittedAt, long? Score)
{
    public int QuestionCount { get; init; }
    public int AnsweredCount { get; init; }
    public TheorySubmissionKind? SubmissionKind { get; init; }
    public TheoryDeadline? Deadline { get; init; }
    public TheoryEffectiveScore? EffectiveScore { get; init; }
}
public sealed record TheoryReviewPage(IReadOnlyList<TheoryReviewRow> Items, int Total);
public sealed record TheoryExamRank(int Rank, Guid UserId, string Name, long Points, DateTimeOffset? AchievedAt, bool HasSubmission);
public sealed record TheoryExamLeaderboard(IReadOnlyList<TheoryExamRank> Items, int Total);
public sealed record TheoryReviewQuestion(TheoryParticipantQuestion Question, Guid RevisionId,
    IReadOnlyList<Guid> CorrectOptions, string? Explanation, TheoryAnswerResult? Result);
public sealed record TheoryReviewPaper(TheoryReviewRow Paper, IReadOnlyList<TheoryReviewQuestion> Questions);
public enum TheoryReviewFailure { NotFoundOrForbidden, InvalidCorrection, PreviewChanged, ReasonRequired, Conflict }
public sealed record TheoryCorrectionInput(Guid RevisionId, TheoryCorrectionMode Mode, IReadOnlyList<Guid> CorrectOptions);
public sealed record TheoryScoreImpact(Guid PaperId, Guid ParticipantId, long Before, long After);
public sealed record TheoryParticipantScoreImpact(Guid ParticipantId, string Name, long Before, long After);
public sealed record TheoryCorrectionPreview(Guid Token, DateTimeOffset ExpiresAt, int AffectedPapers,
    IReadOnlyList<TheoryScoreImpact> Scores)
{
    public IReadOnlyList<TheoryParticipantScoreImpact> Participants { get; init; } = [];
}
public sealed record TheoryCorrectionResult(TheoryCorrectionPreview? Preview = null, Guid? TargetRevision = null, TheoryReviewFailure? Failure = null);
public sealed record TheoryAuditRow(Guid Id, TheoryActivityOperationKind Kind, string ActorName, DateTimeOffset OccurredAt, string? Reason);
public sealed record TheoryAuditPage(IReadOnlyList<TheoryAuditRow> Items, int Total);
public interface ITheoryReviewStore
{
    Task<Guid?> StartConsultationAsync(Guid actorId, Guid activityId, Guid paperId, string body, DateTimeOffset now, CancellationToken ct);
    Task<TheoryAuditPage?> AuditAsync(Guid actorId, Guid activityId, int offset, int limit, DateTimeOffset now, CancellationToken ct);
    Task<TheoryExamLeaderboard?> LeaderboardAsync(Guid actorId, Guid activityId, int offset, int limit, DateTimeOffset now, CancellationToken ct);
    Task<TheoryReviewPage?> ListAsync(Guid actorId, Guid activityId, int offset, int limit, DateTimeOffset now, CancellationToken ct);
    Task<TheoryReviewPaper?> ReadAsync(Guid actorId, Guid activityId, Guid paperId, DateTimeOffset now, CancellationToken ct);
    Task<TheoryCorrectionResult> PreviewAsync(Guid actorId, Guid activityId, TheoryCorrectionInput input, DateTimeOffset now, CancellationToken ct);
    Task<TheoryCorrectionResult> CorrectAsync(Guid actorId, Guid activityId, TheoryCorrectionInput input, Guid token, DateTimeOffset expiresAt,
        string reason, DateTimeOffset now, CancellationToken ct);
    Task<TheoryReviewFailure?> VoidPaperAsync(Guid actorId, Guid activityId, Guid paperId, string reason, DateTimeOffset now, CancellationToken ct);
    Task ApplyAsync(ApplyTheoryCorrection message, DateTimeOffset now, CancellationToken ct);
}
public sealed record ApplyTheoryCorrection(Guid ActivityId, Guid RevisionId, Guid TargetRevision);
