using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awd.Flags;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.GameModes.Awdp.Configuration;
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
        if (activeMatches.Any(flag => flag.TeamId == submission.TeamId))
            return normalDecision;

        var duplicateForeignAttempt = context.PriorFacts.Any(fact =>
            fact.TeamId == submission.TeamId
            && fact.CompetitionChallengeId == submission.CompetitionChallengeId
            && fact.Kind == submission.Kind
            && fact.FailureCode == GameplayFactFailureCode.ForeignTeamFlagDetected
            && string.Equals(fact.Value, submission.Value, StringComparison.Ordinal));
        if (duplicateForeignAttempt)
        {
            return new(
                GameplayFactResult.Duplicate,
                null,
                submission.OccurredAt);
        }

        var attachmentCandidateMatched = activeMatches.Any(flag =>
            flag.TeamId is null
            && flag.SpecificationKind == SpecificationKind.Attachment);
        if (activeMatches.Any(flag => flag.TeamId is null
                && flag.SpecificationKind != SpecificationKind.Attachment))
        {
            return normalDecision;
        }

        var ownerTeamIds = activeMatches
            .Where(flag => flag.TeamId is not null && flag.TeamId != submission.TeamId)
            .Select(flag => flag.TeamId!.Value)
            .Distinct()
            .ToArray();
        if (attachmentCandidateMatched)
        {
            return new(
                GameplayFactResult.Rejected,
                GameplayFactFailureCode.ForeignTeamFlagDetected,
                submission.OccurredAt,
                ownerTeamIds.Length == 1 ? ownerTeamIds[0] : null);
        }
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
        var interactionKind = context.ChallengeDefinition is CtfChallengeDefinition ctf
            ? ctf.InteractionKind
            : CtfInteractionKind.FlagSubmission;
        if (interactionKind == NoCTF.Domain.Challenges.CtfInteractionKind.PatchVerification)
        {
            return ModeGameplayFactEvaluatorRules.Reject(
                context.GameplayFact,
                GameplayFactFailureCode.FlagNotSupported);
        }
        if (context.GameplayFact.Kind != GameplayFactKind.FlagAttempt)
            return ModeGameplayFactEvaluatorRules.Reject(
                context.GameplayFact,
                GameplayFactFailureCode.FixNotSupported);
        var usesRuntimeInjection = context.ChallengeDefinition is CtfChallengeDefinition
            { Runtime: { FlagSource: PersistedRuntimeFlagSource.PerTeam } };
        var effectiveContext = usesRuntimeInjection
            ? context with
            {
                ApplicableFlags = context.ApplicableFlags
                    .Where(flag => flag.MatchKind == ChallengeFlagMatchKind.Exact
                        && flag.TeamId is not null
                        && flag.SpecificationKind == SpecificationKind.RuntimeDefinition
                        && flag.SpecificationId == context.GameplayFact.CompetitionChallengeId)
                    .ToArray()
            }
            : context;
        var decision = ModeGameplayFactEvaluatorRules.DetectForeignTeamFlag(
            effectiveContext, inner.Evaluate(effectiveContext));
        if (decision.Result != GameplayFactResult.Correct
            || context.GameplayFact.AcquisitionEvidence?.MissingEvidence is not { } failure)
            return decision;
        if (context.PriorFacts.Any(fact => CheatIncidentFailures.IsIncident(fact.FailureCode)
                && fact.FailureCode != GameplayFactFailureCode.ForeignTeamFlagDetected
                && fact.TeamId == context.GameplayFact.TeamId
                && fact.CompetitionChallengeId == context.GameplayFact.CompetitionChallengeId
                && fact.Kind == GameplayFactKind.FlagAttempt
                && string.Equals(fact.Value, context.GameplayFact.Value, StringComparison.Ordinal)))
            return new(GameplayFactResult.Duplicate, null, context.GameplayFact.OccurredAt);
        return ModeGameplayFactEvaluatorRules.Reject(context.GameplayFact, failure);
    }

}

public sealed class AwdGameplayFactEvaluator : IGameplayFactEvaluator
{
    public GameplayFactDecision Evaluate(GameplayFactProcessingContext context)
    {
        var submission = context.GameplayFact;
        if (submission.Kind != GameplayFactKind.FlagAttempt)
            return ModeGameplayFactEvaluatorRules.Reject(submission, GameplayFactFailureCode.FixNotSupported);

        if (submission.Awd is not { ReceivedRoundNumber: { } round, ReceivedActiveTicks: { } ticks,
                ReceivedRoundId: not null, ReceiptScorable: not null } receipt)
            return Decision(null, AwdAttackOutcome.SystemError, GameplayFactFailureCode.AwdReceiptUnavailable);
        if (submission.Value is not { Length: > 0 and <= 4096 } value
            || value.Any(character => character is < '!' or > '~'))
            return Decision(GameplayFactResult.Wrong, AwdAttackOutcome.Invalid, GameplayFactFailureCode.FlagInvalid);
        var candidates = context.ApplicableFlags
            .Where(flag => flag.MatchKind == ChallengeFlagMatchKind.Exact)
            .Where(flag => flag.CompetitionChallengeId == submission.CompetitionChallengeId
                && flag.TeamId is not null && string.Equals(flag.Flag, value, StringComparison.Ordinal))
            .OrderBy(flag => flag.Id)
            .ToList();
        if (candidates.Count == 0)
            return Decision(GameplayFactResult.Wrong, AwdAttackOutcome.UnknownFlag, GameplayFactFailureCode.FlagUnknown);
        if (candidates.Count != 1)
            return Decision(null, AwdAttackOutcome.SystemError, GameplayFactFailureCode.AmbiguousFlagMatch);
        var matched = candidates[0];
        if (matched.AwdIssue is not { } issue || issue.CompetitionId != submission.CompetitionId
            || issue.CompetitionChallengeId != submission.CompetitionChallengeId || issue.TeamId != matched.TeamId)
            return Decision(GameplayFactResult.Rejected, AwdAttackOutcome.NotScorable, GameplayFactFailureCode.FlagNotIssued);
        if (issue.State != AwdFlagIssueState.Issued || matched.DeletedAt is not null)
            return Decision(GameplayFactResult.Rejected, AwdAttackOutcome.NotScorable,
                issue.State == AwdFlagIssueState.Revoked ? GameplayFactFailureCode.FlagRevoked : GameplayFactFailureCode.FlagNotIssued, matched);
        if (issue.IssuedAt > submission.OccurredAt || issue.RoundNumber > round || issue.ActiveStartTicks > ticks)
            return Decision(GameplayFactResult.Rejected, AwdAttackOutcome.NotScorable, GameplayFactFailureCode.FlagNotYetActive, matched);
        if (!AwdFlagLifetime.Accepts(issue, round, ticks))
            return Decision(GameplayFactResult.Wrong, AwdAttackOutcome.Expired, GameplayFactFailureCode.FlagExpired, matched);
        if (matched.TeamId == submission.TeamId)
            return Decision(GameplayFactResult.Rejected, AwdAttackOutcome.OwnFlag, GameplayFactFailureCode.SelfAttackRejected, matched);
        if (receipt.ReceiptScorable != true || submission.TimeEligibility != GameplayFactTimeEligibility.Valid)
            return Decision(GameplayFactResult.Rejected, AwdAttackOutcome.NotScorable, GameplayFactFailureCode.FlagNotScorable, matched);
        var issuanceRules = context.AwdIssuanceRules?.SingleOrDefault(rules => rules.Id == issue.RulesId
            && rules.CompetitionId == issue.CompetitionId && rules.CompetitionChallengeId == issue.CompetitionChallengeId);
        if (issuanceRules is null)
            return Decision(null, AwdAttackOutcome.SystemError, GameplayFactFailureCode.AwdReceiptUnavailable, matched);
        if (!issuanceRules.Teams.Any(team => team.TeamId == submission.TeamId && team.Competitive)
            || !issuanceRules.Teams.Any(team => team.TeamId == issue.TeamId && team.Competitive))
            return Decision(GameplayFactResult.Rejected, AwdAttackOutcome.NotScorable, GameplayFactFailureCode.FlagNotScorable, matched);
        // Success uniqueness is checked under the completion transaction by FlagId + attacking team.
        return Decision(GameplayFactResult.Correct, AwdAttackOutcome.Accepted, null, matched);

        GameplayFactDecision Decision(
            GameplayFactResult? result,
            AwdAttackOutcome outcome,
            GameplayFactFailureCode? failure = null,
            ChallengeFlag? matched = null) =>
            new(result, failure, submission.OccurredAt, matched?.TeamId,
                matched?.SpecificationKind == SpecificationKind.AwdRound
                    ? GameplayFactReferenceKind.AwdRound
                    : null,
                matched?.SpecificationId,
                new(outcome, matched?.Id, matched?.AwdIssue?.RoundId, matched?.AwdIssue?.RulesId));
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
        if (context.PriorFacts.Any(fact =>
                fact.Kind == GameplayFactKind.BreakAttempt
                && NoCTF.Domain.Challenges.GameplayFactCompletion.IsSuccessful(fact.Result, fact.TimeEligibility)))
        {
            return new(
                GameplayFactResult.Duplicate,
                GameplayFactFailureCode.DuplicateAchievement,
                submission.OccurredAt);
        }

        var exactContext = context with
        {
            ApplicableFlags = context.ApplicableFlags
                .Where(flag => flag.MatchKind == ChallengeFlagMatchKind.Exact)
                .Where(flag => flag.SpecificationKind == SpecificationKind.RuntimeInstance)
                .ToArray(),
            PriorFacts = []
        };
        var matching = exactContext.ApplicableFlags
            .Where(flag => DefaultEfGameplayFactEvaluator.Matches(submission, flag))
            .ToArray();
        if (matching.Length > 0 && matching.All(flag =>
                flag.ValidStart is null || flag.ValidStart > submission.OccurredAt
                || flag.ValidUntil is not null && submission.OccurredAt >= flag.ValidUntil))
        {
            return new(
                GameplayFactResult.Wrong,
                GameplayFactFailureCode.FlagExpired,
                submission.OccurredAt);
        }
        var normalDecision = inner.Evaluate(exactContext);
        return ModeGameplayFactEvaluatorRules.DetectForeignTeamFlag(exactContext, normalDecision);
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
