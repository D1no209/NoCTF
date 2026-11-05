using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Gameplay;

namespace NoCTF.GameModes.GameplayFact;

public sealed class BreakCapabilityEvaluator(IGameplayFactEvaluator rules) : IGameplayFactEvaluator
{
    public GameplayFactDecision Evaluate(GameplayFactProcessingContext context)
    {
        if (context.GameplayFact.Kind != GameplayFactKind.BreakAttempt) return rules.Evaluate(context);
        var outcome = new EvaluateBreak().Execute(new(context.GameplayFact.Id, context.GameplayFact.Value ?? string.Empty,
            context.ApplicableFlags.Select(flag => new BreakCandidate(flag.Id, flag.Flag, flag.FlagSha256, flag.MatchKind, flag.TeamId)).ToArray(),
            context.GameplayFact.OccurredAt));
        if (!outcome.Succeeded && outcome.FailureCode == NoCTF.Domain.Challenges.Capabilities.CapabilityExecutionFailure.InvalidEvidence)
            throw new InvalidOperationException("The stored Flag matching data is invalid.");
        if (!outcome.Succeeded)
            return new(GameplayFactResult.Wrong, GameplayFactFailureCode.FlagInvalid, context.GameplayFact.OccurredAt);
        var evidence = outcome.Value!.ToGameplayOutcome();
        var decision = rules.Evaluate(context with { BreakEvidence = evidence });
        return decision with { BreakOutcome = evidence };
    }
}
