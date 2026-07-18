using NoCTF.Application.Scoring.Evaluation;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Scoring;
using NoCTF.GameModes.Awdp.Scoring;
using NoCTF.GameModes.Koh.Scoring;
using NoCTF.GameModes.Penetration.Scoring;

namespace NoCTF.GameModes.Scoring;

/// <summary>Compile-time catalog dispatch; mode rules live in their own evaluators.</summary>
public sealed class GameModeScoringEvaluator : IScoringRuleEvaluator
{
    private static readonly IReadOnlyDictionary<GameMode, IGameModeScoringEvaluator> Evaluators =
        new Dictionary<GameMode, IGameModeScoringEvaluator>
        {
            [GameMode.Ctf] = new CtfModeScoringEvaluator(),
            [GameMode.Awd] = new AwdModeScoringEvaluator(),
            [GameMode.Awdp] = new AwdpModeScoringEvaluator(),
            [GameMode.Koh] = new KohModeScoringEvaluator(),
            [GameMode.Penetration] = new PenetrationModeScoringEvaluator()
        };

    public IReadOnlyList<DerivedScoringEvent> Evaluate(
        ScoringContext context,
        IReadOnlyList<SubmissionEventEnvelope> history) =>
        Evaluators.TryGetValue(context.Mode, out var evaluator)
            ? evaluator.Evaluate(context, history)
            : throw new ArgumentOutOfRangeException(nameof(context.Mode), context.Mode, "Unsupported game mode.");
}
