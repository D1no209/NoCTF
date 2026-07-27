using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;
using SubmissionEntity = NoCTF.Domain.Submissions.Submission;

namespace NoCTF.GameModes.Submission;

public sealed class DefaultEfSubmissionEvaluator : ISubmissionEvaluator
{
    public ScoringEventDecision Evaluate(SubmissionProcessingContext context)
    {
        var submission = context.Submission;
        if (submission.Kind == SubmissionKind.Fix)
            return new(ScoringEventKind.SubmissionEvaluation, ScoringResult.PlatformFailed,
                ScoringFailureCode.CheckerPlatformError, submission.ReceivedAt, "fix-runner-required-v1");
        var duplicate = context.PriorEvents.Any(x => x.Kind == ScoringEventKind.SubmissionEvaluation
            && x.TeamId == submission.TeamId
            && x.CompetitionChallengeId == submission.CompetitionChallengeId
            && x.Result == ScoringResult.Correct
            && x.DeletedAt is null);
        var correct = context.ApplicableFlags.Any(x =>
            Matches(submission, x)
            && (x.TeamId is null || x.TeamId == submission.TeamId)
            && (x.ValidStart is null || x.ValidStart <= submission.ReceivedAt)
            && (x.ValidUntil is null || submission.ReceivedAt < x.ValidUntil));
        return new(ScoringEventKind.SubmissionEvaluation, duplicate ? ScoringResult.Duplicate : correct ? ScoringResult.Correct : ScoringResult.Wrong,
            null, submission.ReceivedAt, "ef-v1");
    }

    internal static bool Matches(SubmissionEntity submission, NoCTF.Domain.Challenges.ChallengeFlag flag)
    {
        if (submission.SubmittedFlag is null || submission.SubmittedFlagSha256 is not { Length: 32 }
            || flag.FlagSha256 is not { Length: 32 })
            return false;
        return CryptographicOperations.FixedTimeEquals(submission.SubmittedFlagSha256, flag.FlagSha256)
            && string.Equals(submission.SubmittedFlag, flag.Flag, StringComparison.Ordinal);
    }

    internal static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
