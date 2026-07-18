using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Scoring.Evaluation;

public interface IGameModeScoringEvaluator
{
    GameMode Mode { get; }

    IReadOnlyList<DerivedScoringEvent> Evaluate(
        ScoringContext context,
        IReadOnlyList<SubmissionEventEnvelope> history);
}
