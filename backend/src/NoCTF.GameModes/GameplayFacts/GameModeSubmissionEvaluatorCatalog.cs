using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awd.Configuration;
using SubmissionEntity = NoCTF.Domain.Gameplay.GameplayFact;

namespace NoCTF.GameModes.GameplayFact;

public sealed class GameModeGameplayFactEvaluatorCatalog : IGameplayFactEvaluatorCatalog
{
    private readonly IReadOnlyDictionary<GameMode, IGameplayFactEvaluator> evaluators =
        new Dictionary<GameMode, IGameplayFactEvaluator>
        {
            [GameMode.Ctf] = new CtfGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator()),
            [GameMode.Awd] = new AwdGameplayFactEvaluator(),
            [GameMode.Awdp] = new AwdpGameplayFactEvaluator(new DefaultEfGameplayFactEvaluator()),
            [GameMode.Koh] = new KohGameplayFactEvaluator()
        };

    public IGameplayFactEvaluator Get(GameMode mode) => evaluators[mode];
}

internal static class ModeGameplayFactEvaluatorRules
{
    public static GameplayFactDecision Reject(SubmissionEntity submission, GameplayFactFailureCode code) =>
        new(GameplayFactResult.Rejected, code, submission.OccurredAt);

    public static GameplayFactDecision DetectForeignTeamFlag(
        GameplayFactProcessingContext context,
        GameplayFactDecision normalDecision)
    {
        var submission = context.GameplayFact;
        var activeMatches = context.ApplicableFlags
            .Where(flag => DefaultEfGameplayFactEvaluator.Matches(submission, flag))
            .Where(flag => flag.ValidStart is null || flag.ValidStart <= submission.OccurredAt)
            .Where(flag => flag.ValidUntil is null || submission.OccurredAt < flag.ValidUntil)
            .ToArray();
        if (activeMatches.Any(flag => flag.TeamId is null || flag.TeamId == submission.TeamId))
            return normalDecision;

        var ownerTeamIds = activeMatches
            .Where(flag => flag.TeamId is not null && flag.TeamId != submission.TeamId)
            .Select(flag => flag.TeamId!.Value)
            .Distinct()
            .ToArray();
        return ownerTeamIds.Length switch
        {
            0 => normalDecision,
            1 => new(
                GameplayFactResult.Rejected,
                GameplayFactFailureCode.ForeignTeamFlagDetected,
                submission.OccurredAt,
                ownerTeamIds[0]),
            _ => new(
                GameplayFactResult.Rejected,
                GameplayFactFailureCode.AmbiguousFlagMatch,
                submission.OccurredAt)
        };
    }
}

public sealed class CtfGameplayFactEvaluator(IGameplayFactEvaluator inner) : IGameplayFactEvaluator
{
    public GameplayFactDecision Evaluate(GameplayFactProcessingContext context)
    {
        if (context.GameplayFact.Kind != GameplayFactKind.FlagAttempt)
            return ModeGameplayFactEvaluatorRules.Reject(
                context.GameplayFact,
                GameplayFactFailureCode.FixNotSupported);
        return ModeGameplayFactEvaluatorRules.DetectForeignTeamFlag(
            context,
            inner.Evaluate(context));
    }
}

public sealed class AwdGameplayFactEvaluator : IGameplayFactEvaluator
{
    public GameplayFactDecision Evaluate(GameplayFactProcessingContext context)
    {
        var submission = context.GameplayFact;
        if (submission.Kind != GameplayFactKind.FlagAttempt)
            return ModeGameplayFactEvaluatorRules.Reject(submission, GameplayFactFailureCode.FixNotSupported);

        var configuration = AwdConfigurationUpgrader.ParseCompetition(context.CompetitionConfigurationJson);
        if (context.EffectiveRunningTime is TimeSpan effectiveRunningTime
            && effectiveRunningTime < TimeSpan.FromSeconds(configuration.HardeningDurationSeconds))
            return ModeGameplayFactEvaluatorRules.Reject(submission, GameplayFactFailureCode.HardeningActive);

        var candidates = context.ApplicableFlags
            .Where(flag => flag.TeamId is not null && DefaultEfGameplayFactEvaluator.Matches(submission, flag))
            .OrderBy(flag => flag.Id)
            .ToList();
        if (candidates.Count == 0)
            return Decision(GameplayFactResult.Wrong);

        var valid = candidates.FirstOrDefault(flag =>
            (flag.ValidStart is null || flag.ValidStart <= submission.OccurredAt)
            && (flag.ValidUntil is null || submission.OccurredAt < flag.ValidUntil));
        if (valid is null)
            return Decision(GameplayFactResult.Wrong, GameplayFactFailureCode.FlagExpired);
        if (valid.TeamId == submission.TeamId)
            return ModeGameplayFactEvaluatorRules.Reject(submission, GameplayFactFailureCode.SelfAttackRejected);

        var duplicate = context.PriorFacts.Any(fact =>
            fact.TeamId == submission.TeamId
            && fact.VictimTeamId == valid.TeamId
            && fact.CompetitionChallengeId == submission.CompetitionChallengeId
            && fact.ReferenceKind == GameplayFactReferenceKind.AwdRound
            && fact.ReferenceId == valid.SpecificationId
            && fact.Result == GameplayFactResult.Correct);
        return duplicate
            ? Decision(GameplayFactResult.Duplicate, GameplayFactFailureCode.DuplicateAttack, valid)
            : Decision(GameplayFactResult.Correct, null, valid);

        GameplayFactDecision Decision(
            GameplayFactResult result,
            GameplayFactFailureCode? failure = null,
            ChallengeFlag? matched = null) =>
            new(result, failure, submission.OccurredAt, matched?.TeamId,
                matched?.SpecificationKind == SpecificationKind.AwdRound
                    ? GameplayFactReferenceKind.AwdRound
                    : null,
                matched?.SpecificationId);
    }
}

public sealed class AwdpGameplayFactEvaluator(IGameplayFactEvaluator inner) : IGameplayFactEvaluator
{
    public GameplayFactDecision Evaluate(GameplayFactProcessingContext context)
    {
        var submission = context.GameplayFact;
        if (submission.Kind == GameplayFactKind.FlagAttempt)
            return ModeGameplayFactEvaluatorRules.Reject(submission, GameplayFactFailureCode.FlagNotSupported);
        if (submission.Kind != GameplayFactKind.BreakAttempt)
            return inner.Evaluate(context);

        var normalDecision = inner.Evaluate(context with { PriorFacts = [] });
        return ModeGameplayFactEvaluatorRules.DetectForeignTeamFlag(context, normalDecision);
    }
}

public sealed class KohGameplayFactEvaluator : IGameplayFactEvaluator
{
    public GameplayFactDecision Evaluate(GameplayFactProcessingContext context) =>
        ModeGameplayFactEvaluatorRules.Reject(context.GameplayFact,
            context.GameplayFact.Kind == GameplayFactKind.FixAttempt
                ? GameplayFactFailureCode.FixNotSupported
                : GameplayFactFailureCode.FlagNotSupported);
}
