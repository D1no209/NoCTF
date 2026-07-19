using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Common;

namespace NoCTF.Application.Submissions.Retry;

public interface ISubmissionRetryStore
{
    Task<SubmissionRetryResult> RetireCurrentEventAsync(Guid competitionId, Guid submissionId, Guid actorId, bool allowCorrect, CancellationToken cancellationToken);
}

public sealed record SubmissionRetryResult(bool Found, bool Retried, string? ErrorCode = null);

public sealed class RetrySubmission(ISubmissionRetryStore store, IBackgroundWorkScheduler scheduler)
{
    public async Task<OperationResult> ExecuteAsync(Guid competitionId, Guid submissionId, Guid actorId, bool allowCorrect, CancellationToken cancellationToken)
    {
        var result = await store.RetireCurrentEventAsync(competitionId, submissionId, actorId, allowCorrect, cancellationToken);
        if (!result.Found) return OperationResult.Failure("submission_not_found", "Submission was not found.");
        if (!result.Retried) return OperationResult.Failure(result.ErrorCode ?? "retry_rejected", "Submission cannot be retried.");
        await scheduler.EnqueueSubmissionAsync(submissionId, cancellationToken);
        await scheduler.EnqueueLeaderboardRefreshAsync(competitionId, cancellationToken);
        return OperationResult.Success();
    }
}
