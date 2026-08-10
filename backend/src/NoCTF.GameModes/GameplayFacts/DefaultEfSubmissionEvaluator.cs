using System.Security.Cryptography;
using System.Text;
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
        var duplicate = context.PriorFacts.Any(x => x.TeamId == submission.TeamId
            && x.CompetitionChallengeId == submission.CompetitionChallengeId
            && x.Result == GameplayFactResult.Correct);
        var correct = context.ApplicableFlags.Any(x =>
            Matches(submission, x)
            && (x.TeamId is null || x.TeamId == submission.TeamId)
            && (x.ValidStart is null || x.ValidStart <= submission.OccurredAt)
            && (x.ValidUntil is null || submission.OccurredAt < x.ValidUntil));
        return new(duplicate ? GameplayFactResult.Duplicate : correct ? GameplayFactResult.Correct : GameplayFactResult.Wrong,
            null, submission.OccurredAt);
    }

    internal static bool Matches(SubmissionEntity submission, NoCTF.Domain.Challenges.ChallengeFlag flag)
    {
        if (submission.Value is null || submission.ValueSha256 is not { Length: 32 }
            || flag.FlagSha256 is not { Length: 32 })
            return false;
        return CryptographicOperations.FixedTimeEquals(submission.ValueSha256, flag.FlagSha256)
            && string.Equals(submission.Value, flag.Flag, StringComparison.Ordinal);
    }

    internal static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
