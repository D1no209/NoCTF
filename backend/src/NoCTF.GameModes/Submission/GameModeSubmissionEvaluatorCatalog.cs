using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;
using SubmissionEntity = NoCTF.Domain.Submissions.Submission;

namespace NoCTF.GameModes.Submission;

public sealed class GameModeSubmissionEvaluatorCatalog : ISubmissionEvaluatorCatalog
{
    private readonly IReadOnlyDictionary<GameMode, ISubmissionEvaluator> evaluators =
        new Dictionary<GameMode, ISubmissionEvaluator>
        {
            [GameMode.Ctf] = new CtfSubmissionEvaluator(new DefaultEfSubmissionEvaluator()),
            [GameMode.Awd] = new AwdSubmissionEvaluator(new DefaultEfSubmissionEvaluator()),
            [GameMode.Awdp] = new AwdpSubmissionEvaluator(new DefaultEfSubmissionEvaluator()),
            [GameMode.Koh] = new KohSubmissionEvaluator(),
            [GameMode.Penetration] = new PenetrationSubmissionEvaluator(new DefaultEfSubmissionEvaluator())
        };

    public ISubmissionEvaluator Get(GameMode mode) => evaluators[mode];
}

internal static class ModeSubmissionEvaluatorRules
{
    public static ScoringEventDecision Reject(SubmissionEntity submission, ScoringFailureCode code) =>
        new(
            ScoringEventKind.SubmissionEvaluation,
            ScoringResult.Rejected,
            code,
            submission.ReceivedAt,
            "mode-admission-v1");
}

public sealed class CtfSubmissionEvaluator(ISubmissionEvaluator inner) : ISubmissionEvaluator
{
    public ScoringEventDecision Evaluate(SubmissionProcessingContext context) =>
        context.Submission.Kind == SubmissionKind.Fix
            ? ModeSubmissionEvaluatorRules.Reject(context.Submission, ScoringFailureCode.FixNotSupported)
            : inner.Evaluate(context);
}

public sealed class AwdSubmissionEvaluator(ISubmissionEvaluator inner) : ISubmissionEvaluator
{
    public ScoringEventDecision Evaluate(SubmissionProcessingContext context)
    {
        if (context.Submission.Kind == SubmissionKind.Fix)
            return ModeSubmissionEvaluatorRules.Reject(context.Submission, ScoringFailureCode.FixNotSupported);
        if (context.Submission.SubjectTeamId is { } subject && subject == context.Submission.TeamId
            || context.Submission.VictimTeamId is { } victim && victim == context.Submission.TeamId)
            return ModeSubmissionEvaluatorRules.Reject(context.Submission, ScoringFailureCode.SelfAttackRejected);
        var duplicate = context.PriorSubmissions?.Any(previous =>
            previous.Kind == SubmissionKind.Flag
            && previous.TeamId == context.Submission.TeamId
            && previous.SubjectTeamId == context.Submission.SubjectTeamId
            && previous.VictimTeamId == context.Submission.VictimTeamId
            && previous.ServiceId == context.Submission.ServiceId
            && context.PriorEvents.Any(@event => @event.SubmissionId == previous.Id && @event.Result == ScoringResult.Correct)) == true;
        return duplicate
            ? new ScoringEventDecision(
                ScoringEventKind.SubmissionEvaluation,
                ScoringResult.Duplicate,
                ScoringFailureCode.DuplicateAttack,
                context.Submission.ReceivedAt,
                "awd-evaluator-v1")
            : inner.Evaluate(context);
    }
}

public sealed class AwdpSubmissionEvaluator(ISubmissionEvaluator inner) : ISubmissionEvaluator
{
    public ScoringEventDecision Evaluate(SubmissionProcessingContext context) => inner.Evaluate(context);
}

public sealed class KohSubmissionEvaluator : ISubmissionEvaluator
{
    public ScoringEventDecision Evaluate(SubmissionProcessingContext context) =>
        ModeSubmissionEvaluatorRules.Reject(
            context.Submission,
            context.Submission.Kind == SubmissionKind.Fix
                ? ScoringFailureCode.FixNotSupported
                : ScoringFailureCode.FlagNotSupported);
}

public sealed class PenetrationSubmissionEvaluator(ISubmissionEvaluator inner) : ISubmissionEvaluator
{
    public ScoringEventDecision Evaluate(SubmissionProcessingContext context) =>
        context.Submission.Kind == SubmissionKind.Fix
            ? ModeSubmissionEvaluatorRules.Reject(context.Submission, ScoringFailureCode.FixNotSupported)
            : inner.Evaluate(context);
}
