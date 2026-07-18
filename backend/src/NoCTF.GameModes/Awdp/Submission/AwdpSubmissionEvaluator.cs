using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Submission;

namespace NoCTF.GameModes.Awdp.Submission;

public sealed class AwdpSubmissionEvaluator : IGameModeFlagSubmissionEvaluator, IGameModeFixSubmissionEvaluator
{
    public GameMode Mode => GameMode.Awdp;

    public SubmissionEvaluationResult EvaluateFlag(FlagSubmissionReceived submission, SubmissionEvaluationContext context)
    {
        var round = SubmissionRoundCalculator.Calculate(submission.ReceivedAt, context.CompetitionStart, context.RoundDurationSeconds);
        var challenge = AwdpConfigurationUpgrader.ParseChallenge(context.ChallengeConfigurationJson);
        var failures = context.History
            .Where(item => item.Sequence < context.CurrentSubmissionSequence)
            .Select(item => item.Event)
            .OfType<FlagSubmissionEvaluated>()
            .Count(item => item.TeamId == submission.TeamId
                && item.ChallengeId == submission.ChallengeId
                && item.ConsumedAttempt);
        if (failures >= challenge.MaxBreakAttempts)
            return new(SubmissionOutcome.AttemptsExhausted, false, SubmissionErrorCode.BreakAttemptsExhausted, Round: round);
        var correct = string.Equals(submission.Flag, context.ExpectedFlag, StringComparison.Ordinal);
        return new(correct ? SubmissionOutcome.Correct : SubmissionOutcome.Wrong, !correct, Round: round);
    }

    public SubmissionEvaluationResult EvaluateFix(FixSubmissionReceived submission, SubmissionEvaluationContext context)
    {
        var round = SubmissionRoundCalculator.Calculate(submission.ReceivedAt, context.CompetitionStart, context.RoundDurationSeconds);
        var challenge = AwdpConfigurationUpgrader.ParseChallenge(context.ChallengeConfigurationJson);
        var failures = context.History
            .Where(item => item.Sequence < context.CurrentSubmissionSequence)
            .Select(item => item.Event)
            .OfType<FixSubmissionEvaluated>()
            .Count(item => item.TeamId == submission.TeamId
                && item.ChallengeId == submission.ChallengeId
                && item.ConsumedAttempt);
        if (failures >= challenge.MaxFixAttempts)
            return new(SubmissionOutcome.AttemptsExhausted, false, SubmissionErrorCode.FixAttemptsExhausted, Round: round);
        if (challenge.RequireBreakBeforeFix && !context.History
                .Where(item => item.Sequence < context.CurrentSubmissionSequence)
                .Select(item => item.Event)
                .OfType<FlagSubmissionEvaluated>()
                .Any(item => item.TeamId == submission.TeamId
                    && item.ChallengeId == submission.ChallengeId
                    && item.Outcome == SubmissionOutcome.Correct))
            return new(SubmissionOutcome.Rejected, false, SubmissionErrorCode.BreakRequired, Round: round);
        var archive = context.Archive;
        if (archive is null || archive.Status == ArchiveValidationStatus.PlatformFailed)
            return new(SubmissionOutcome.PlatformFailed, false,
                archive?.ErrorCode ?? SubmissionErrorCode.ArchiveValidationUnavailable, Round: round);
        if (archive.Status != ArchiveValidationStatus.Valid)
            return new(SubmissionOutcome.Wrong, true, archive.ErrorCode, Round: round);
        return new(SubmissionOutcome.Correct, false, Round: round);
    }
}
