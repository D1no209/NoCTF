using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Gameplay;
using SubmissionEntity = NoCTF.Domain.Gameplay.GameplayFact;

namespace NoCTF.GameModes.GameplayFact;

public sealed class DefaultEfGameplayFactEvaluator : IGameplayFactEvaluator
{
    public GameplayFactDecision Evaluate(GameplayFactProcessingContext context)
    {
        var submission = context.GameplayFact;
        if (submission.Kind == GameplayFactKind.FixAttempt)
            return new(null, GameplayFactFailureCode.CheckerPlatformError, submission.OccurredAt);
        var correct = context.ApplicableFlags.Any(x =>
            Matches(submission, x)
            && (x.TeamId is null || x.TeamId == submission.TeamId)
            && (x.ValidStart is null || x.ValidStart <= submission.OccurredAt)
            && (x.ValidUntil is null || submission.OccurredAt < x.ValidUntil));
        return new(correct ? GameplayFactResult.Correct : GameplayFactResult.Wrong,
            null, submission.OccurredAt);
    }

    internal static bool Matches(SubmissionEntity submission, NoCTF.Domain.Challenges.ChallengeFlag flag)
    {
        if (submission.Value is null)
            return false;
        return ChallengeFlagMatcher.IsMatch(submission.Value, flag);
    }

    internal static byte[] Hash(string value) => ManageChallengeFlags.Hash(value);
}
