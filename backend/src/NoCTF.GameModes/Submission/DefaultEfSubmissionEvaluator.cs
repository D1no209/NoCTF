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
            var result = context.FixRecord?.VerificationStatus == NoCTF.Domain.Submissions.FixVerificationStatus.Valid
                ? ScoringResult.Correct : ScoringResult.Rejected;
            return new(ScoringEventKind.SubmissionEvaluation, result,
                result == ScoringResult.Correct ? null : ScoringFailureCode.FixArchiveMissing,
                submission.ReceivedAt, "ef-v1");
        }
        var duplicate = context.PriorEvents.Any(x => x.TeamId == submission.TeamId && x.ChallengeId == submission.ChallengeId && x.Result == ScoringResult.Correct);
        var correct = submission.Flag is not null && context.ApplicableFlags.Any(x => x.Flag == submission.Flag && (x.TeamId is null || x.TeamId == submission.TeamId));
        return new(ScoringEventKind.SubmissionEvaluation, duplicate ? ScoringResult.Duplicate : correct ? ScoringResult.Correct : ScoringResult.Wrong,
            null, submission.ReceivedAt, "ef-v1");
    }
}
