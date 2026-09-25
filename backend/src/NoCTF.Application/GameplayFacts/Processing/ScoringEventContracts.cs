using NoCTF.Domain.Challenges;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.GameplayFacts.Processing;

public sealed record GameplayFactDecision(
    GameplayFactResult? Result,
    GameplayFactFailureCode? FailureCode,
    DateTimeOffset OccurredAt,
    Guid? VictimTeamId = null,
    GameplayFactReferenceKind? ReferenceKind = null,
    Guid? ReferenceId = null);

public sealed record GameplayFactProcessingContext(
    GameplayFact GameplayFact,
    IReadOnlyList<GameplayFact> PriorFacts,
    IReadOnlyList<ChallengeFlag> ApplicableFlags,
    PatchUpload? PatchUpload,
    CompetitionModeConfiguration CompetitionConfiguration,
    CompetitionChallengeRules ChallengeRules,
    DateTimeOffset? CompetitionStartTime = null,
    TimeSpan? EffectiveRunningTime = null,
    ChallengeDefinition? ChallengeDefinition = null);

public interface IGameplayFactEvaluator
{
    GameplayFactDecision Evaluate(GameplayFactProcessingContext context);
}

public interface IGameplayFactEvaluatorCatalog
{
    IGameplayFactEvaluator Get(GameMode mode);
}
