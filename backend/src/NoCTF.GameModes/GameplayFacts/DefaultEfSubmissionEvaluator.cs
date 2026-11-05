using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Gameplay;

namespace NoCTF.GameModes.GameplayFact;

public sealed class DefaultEfGameplayFactEvaluator : IGameplayFactEvaluator
{
    public GameplayFactDecision Evaluate(GameplayFactProcessingContext context)
    {
        var submission = context.GameplayFact;
        if (submission.Kind == GameplayFactKind.FixAttempt)
            return new(null, GameplayFactFailureCode.CheckerPlatformError, submission.OccurredAt);
        var correct = context.ApplicableFlags.Any(x =>
            Matches(context, x)
            && (x.TeamId is null || x.TeamId == submission.TeamId)
            && (x.ValidStart is null || x.ValidStart <= submission.OccurredAt)
            && (x.ValidUntil is null || submission.OccurredAt < x.ValidUntil));
        return new(correct ? GameplayFactResult.Correct : GameplayFactResult.Wrong,
            null, submission.OccurredAt);
    }

    internal static bool Matches(GameplayFactProcessingContext context, NoCTF.Domain.Challenges.ChallengeFlag flag)
    {
        var evidence = context.BreakEvidence ?? throw new InvalidOperationException("Break evidence must be evaluated before applying competition rules.");
        return evidence.Flags.Any(match => match.FlagId == flag.Id);
    }
}
