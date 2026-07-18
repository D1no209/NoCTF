using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Awd.Submission;

public sealed class AwdSubmissionEvaluator : IGameModeFlagSubmissionEvaluator
{
    public GameMode Mode => GameMode.Awd;

    public SubmissionEvaluationResult EvaluateFlag(FlagSubmissionReceived submission, SubmissionEvaluationContext context)
    {
        var candidate = context.AwdFlags
            .Where(item => item.TeamId != submission.TeamId && item.ChallengeId == submission.ChallengeId
                && item.Flag == submission.Flag && item.OccurredAt <= submission.ReceivedAt)
            .OrderByDescending(item => item.OccurredAt)
            .FirstOrDefault();
        if (candidate is null) return new(SubmissionOutcome.Wrong, true);
        var duplicate = context.History.Select(item => item.Event).OfType<FlagSubmissionEvaluated>()
            .Any(item => item.TeamId == submission.TeamId && item.ChallengeId == submission.ChallengeId && item.Outcome == SubmissionOutcome.Correct);
        return duplicate
            ? new(SubmissionOutcome.Duplicate, false, OriginalRound: candidate.Round)
            : new(SubmissionOutcome.Correct, false, OriginalRound: candidate.Round);
    }
}
