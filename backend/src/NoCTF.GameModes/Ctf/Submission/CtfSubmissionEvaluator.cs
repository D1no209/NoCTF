using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Ctf.Submission;

public sealed class CtfSubmissionEvaluator : IGameModeFlagSubmissionEvaluator
{
    public GameMode Mode => GameMode.Ctf;

    public SubmissionEvaluationResult EvaluateFlag(FlagSubmissionReceived submission, SubmissionEvaluationContext context)
    {
        var priorCorrect = context.History.Select(item => item.Event).OfType<FlagSubmissionEvaluated>()
            .Any(item => item.ChallengeId == submission.ChallengeId && item.TeamId == submission.TeamId && item.Outcome == SubmissionOutcome.Correct);
        var outcome = priorCorrect
                ? SubmissionOutcome.Duplicate
                : string.Equals(submission.Flag, context.ExpectedFlag, StringComparison.Ordinal)
                    ? SubmissionOutcome.Correct
                    : SubmissionOutcome.Wrong;
        return new(outcome, outcome == SubmissionOutcome.Wrong);
    }
}
