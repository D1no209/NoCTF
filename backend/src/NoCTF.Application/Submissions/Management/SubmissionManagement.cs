using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Management;

public sealed record SubmissionListFilter(
    Guid CompetitionId,
    Guid? CompetitionChallengeId,
    Guid? TeamId,
    Guid? UserId,
    SubmissionKind? Kind,
    SubmissionEvaluationState? EvaluationState,
    ScoringResult? Result,
    ScoringFailureCode? FailureCode,
    DateTimeOffset? ReceivedFrom,
    DateTimeOffset? ReceivedTo,
    string? SubmittedFlag,
    bool? HasCurrentScoringEvent);

public sealed record SubmissionListItem(
    Guid Id,
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid TeamId,
    Guid SubmittedByUserId,
    SubmissionKind Kind,
    SubmissionEvaluationState EvaluationState,
    ScoringResult? Result,
    ScoringFailureCode? FailureCode,
    DateTimeOffset ReceivedAt,
    long ProcessingVersion);

public interface ISubmissionManagementStore
{
    Task<IReadOnlyList<SubmissionListItem>> ListAdminAsync(
        SubmissionListFilter filter,
        DateTimeOffset? beforeReceivedAt,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SubmissionListItem>?> ListPlayerAsync(
        Guid competitionId,
        Guid userId,
        DateTimeOffset? beforeReceivedAt,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken);
    Task QueueDrainAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset cutoff,
        bool rejudge,
        Guid? submissionId,
        CancellationToken cancellationToken);
}

public sealed class ListSubmissions(ISubmissionManagementStore store)
{
    public Task<IReadOnlyList<SubmissionListItem>> AdminAsync(
        SubmissionListFilter filter,
        DateTimeOffset? beforeReceivedAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct = default) =>
        store.ListAdminAsync(filter, beforeReceivedAt, beforeId, limit, ct);

    public Task<IReadOnlyList<SubmissionListItem>?> PlayerAsync(
        Guid competitionId,
        Guid userId,
        DateTimeOffset? beforeReceivedAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct = default) =>
        store.ListPlayerAsync(competitionId, userId, beforeReceivedAt, beforeId, limit, ct);
}

public sealed class QueueSubmissionWork(ISubmissionManagementStore store)
{
    public Task QueueAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset cutoff,
        bool rejudge,
        Guid? submissionId,
        CancellationToken ct = default) =>
        store.QueueDrainAsync(
            competitionId, competitionChallengeId, cutoff, rejudge, submissionId, ct);
}
