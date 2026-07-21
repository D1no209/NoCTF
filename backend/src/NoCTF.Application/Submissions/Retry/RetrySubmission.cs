using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Submissions.Retry;

public interface ISubmissionRetryStore
{
    Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<SubmissionRetryResult> RetireCurrentEventAsync(Guid competitionId, Guid submissionId, Guid actorId, bool allowCorrect, CancellationToken cancellationToken);
}

public enum SubmissionRetryFailure
{
    CompetitionNotFound,
    CompetitionFinished,
    CorrectSubmissionCannotRetry,
    FixVerificationCannotRetry
}

public sealed record SubmissionRetryResult(bool Found, bool Retried, SubmissionRetryFailure? Failure = null);

public sealed class RetrySubmission(ISubmissionRetryStore store, IBackgroundWorkScheduler scheduler)
{
    public async Task<OperationResult> ExecuteAsync(Guid competitionId, Guid submissionId, Guid actorId, bool allowCorrect, CancellationToken cancellationToken)
    {
        var status = await store.GetCompetitionStatusAsync(competitionId, cancellationToken);
        if (status is null) return OperationResult.Failure("competition_not_found", "Competition was not found.");
        if (status == CompetitionStatus.Finished)
            return OperationResult.Failure("competition_finished", "Finished competitions are read-only.");
        var result = await store.RetireCurrentEventAsync(competitionId, submissionId, actorId, allowCorrect, cancellationToken);
        if (!result.Found) return OperationResult.Failure("submission_not_found", "Submission was not found.");
        if (!result.Retried) return result.Failure switch
        {
            SubmissionRetryFailure.CompetitionNotFound => OperationResult.Failure("competition_not_found", "Competition was not found."),
            SubmissionRetryFailure.CompetitionFinished => OperationResult.Failure("competition_finished", "Finished competitions are read-only."),
            SubmissionRetryFailure.CorrectSubmissionCannotRetry => OperationResult.Failure("correct_submission_cannot_retry", "A correct submission cannot be retried."),
            SubmissionRetryFailure.FixVerificationCannotRetry => OperationResult.Failure("fix_verification_cannot_retry", "Fix verification cannot be retried."),
            _ => OperationResult.Failure("retry_rejected", "Submission cannot be retried.")
        };
        await scheduler.EnqueueSubmissionAsync(submissionId, cancellationToken);
        await scheduler.EnqueueLeaderboardRefreshAsync(competitionId, cancellationToken);
        return OperationResult.Success();
    }
}
