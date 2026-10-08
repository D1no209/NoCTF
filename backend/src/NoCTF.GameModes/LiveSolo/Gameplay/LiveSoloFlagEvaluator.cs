using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.GameplayFact;

namespace NoCTF.GameModes.LiveSolo.Gameplay;

/// <summary>Reuses the protected Flag matcher and ownership rules; Round ordering is a separate use case.</summary>
public sealed class LiveSoloFlagEvaluator : IGameplayFactEvaluator
{
    private readonly DefaultEfGameplayFactEvaluator matcher = new();
    public GameplayFactDecision Evaluate(GameplayFactProcessingContext context)
    {
        var normal = matcher.Evaluate(context);
        var ownership = ModeGameplayFactEvaluatorRules.DetectForeignTeamFlag(context, normal);
        if (ownership.Result != GameplayFactResult.Correct) return ownership;
        return context.GameplayFact.AcquisitionEvidence?.MissingEvidence is { } missing
            ? new(GameplayFactResult.Rejected, missing, context.GameplayFact.OccurredAt) : ownership;
    }
}
