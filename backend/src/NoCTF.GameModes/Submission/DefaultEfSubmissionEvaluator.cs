using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;

namespace NoCTF.GameModes.Submission;

public sealed class DefaultEfSubmissionEvaluator : ISubmissionEvaluator
{
    public ScoringEventDecision Evaluate(SubmissionProcessingContext context)
    {
        var submission = context.Submission;
        if (submission.Kind == SubmissionKind.Fix)
        {
            var result = context.FixRecord?.VerificationStatus switch
            {
                NoCTF.Domain.Submissions.FixVerificationStatus.Valid => ScoringResult.Correct,
                NoCTF.Domain.Submissions.FixVerificationStatus.PlatformFailed => ScoringResult.PlatformFailed,
                _ => ScoringResult.Rejected
            };
            return new(ScoringEventKind.SubmissionEvaluation, result,
                result switch
                {
                    ScoringResult.Correct => null,
                    ScoringResult.PlatformFailed => context.FixRecord?.FailureCategory ?? ScoringFailureCode.StorageUnavailable,
                    _ => ScoringFailureCode.FixArchiveMissing
                },
                submission.ReceivedAt, "ef-v1");
        }
        var duplicate = context.PriorEvents.Any(x => x.TeamId == submission.TeamId && x.ChallengeId == submission.ChallengeId && x.Result == ScoringResult.Correct);
        var correct = submission.Flag is not null && context.ApplicableFlags.Any(x =>
            x.Flag == submission.Flag
            && (x.TeamId is null || x.TeamId == submission.TeamId)
            && (x.ValidStart is null || x.ValidStart <= submission.ReceivedAt)
            && (x.ValidEnd is null || x.ValidEnd >= submission.ReceivedAt));
        return new(ScoringEventKind.SubmissionEvaluation, duplicate ? ScoringResult.Duplicate : correct ? ScoringResult.Correct : ScoringResult.Wrong,
            null, submission.ReceivedAt, "ef-v1");
    }
}
