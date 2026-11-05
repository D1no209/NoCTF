using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Scoring;
using NoCTF.GameModes.Flags;
using NoCTF.GameModes.Scoring;
using NoCTF.Domain.Challenges;

namespace NoCTF.GameModes.Ctf.Configuration;

public static class CtfConfigurationValidator
{
    private static readonly ScoreCurveEvaluator ScoreCurve = new();

    public static IReadOnlyList<string> Validate(CtfConfiguration configuration) => Validate(configuration, 1);

    public static IReadOnlyList<string> Validate(CtfConfiguration configuration, int eligibleTeamCount)
    {
        var errors = ScoreCurve.Validate(configuration.DefaultScoreCurve, eligibleTeamCount).ToList();
        if (!Enum.IsDefined(configuration.ScoreSettlementMode))
            errors.Add("ScoreSettlementMode is invalid.");
        ValidateNextPrice(configuration.DefaultScoreCurve, configuration.ScoreSettlementMode, eligibleTeamCount, errors);
        if (configuration.WrongSubmissionPenalty is < 0 or > ScoreValueLimits.MaximumConfiguredValue)
            errors.Add($"WrongSubmissionPenalty must be between zero and {ScoreValueLimits.MaximumConfiguredValue}.");
        if (configuration.BloodRewards.Count > 3)
            errors.Add("At most three blood rewards are supported.");
        foreach (var reward in configuration.BloodRewards)
        {
            if (reward.Value < 0)
                errors.Add("Blood reward values cannot be negative.");
            if (reward.Policy == BloodRewardPolicy.FixedPoints
                && reward.Value > ScoreValueLimits.MaximumConfiguredValue)
                errors.Add($"Fixed blood rewards cannot exceed {ScoreValueLimits.MaximumConfiguredValue}.");
            if (reward.Policy != BloodRewardPolicy.FixedPoints && reward.Value > 100)
                errors.Add("Blood reward percentages cannot exceed 100.");
        }
        ValidateFlagTemplate(configuration.FlagTemplate, errors);
        return errors;
    }

    public static IReadOnlyList<string> Validate(CtfChallengeConfiguration configuration) =>
        Validate(configuration, null, 1);

    public static IReadOnlyList<string> Validate(
        CtfChallengeConfiguration configuration,
        CtfConfiguration? competitionConfiguration,
        int eligibleTeamCount)
    {
        var errors = configuration.ScoreCurve is null
            ? []
            : ScoreCurve.Validate(configuration.ScoreCurve, eligibleTeamCount).ToList();
        if (configuration.ScoreSettlementMode is { } settlement && !Enum.IsDefined(settlement))
            errors.Add("ScoreSettlementMode is invalid.");
        if (configuration.WrongSubmissionPenalty is < 0 or > ScoreValueLimits.MaximumConfiguredValue)
            errors.Add($"WrongSubmissionPenalty must be between zero and {ScoreValueLimits.MaximumConfiguredValue}.");
        if (configuration.BloodRewards is { } rewards)
        {
            if (rewards.Count > 3)
                errors.Add("At most three blood rewards are supported.");
            foreach (var reward in rewards)
            {
                if (reward.Value < 0)
                    errors.Add("Blood reward values cannot be negative.");
                if (reward.Policy == BloodRewardPolicy.FixedPoints
                    && reward.Value > ScoreValueLimits.MaximumConfiguredValue)
                    errors.Add($"Fixed blood rewards cannot exceed {ScoreValueLimits.MaximumConfiguredValue}.");
                if (reward.Policy != BloodRewardPolicy.FixedPoints && reward.Value > 100)
                    errors.Add("Blood reward percentages cannot exceed 100.");
            }
        }
        ValidateMaxAttempts(configuration.MaxFlagAttempts, errors);
        ValidateFlagTemplate(configuration.FlagTemplate, errors);
        errors.AddRange(Registration.ChallengeRuntimeTemplateValidator.Validate(configuration.Runtime));
        if (configuration.Runtime is { Allocation: not RuntimeAllocation.PerTeam })
            errors.Add("CTF runtimes must use PerTeam allocation.");
        if (configuration.Runtime is
            {
                FlagSource: not RuntimeFlagSource.Static and not RuntimeFlagSource.PerTeam
            })
        {
            errors.Add("CTF runtimes support only Static or PerTeam flags.");
        }
        if ((configuration.Runtime?.UrlBindings ?? [])
            .Any(binding => binding is not null
                && binding.Exposure != RuntimeExposure.OwnerOnly))
        {
            errors.Add("CTF runtime URL bindings must use OwnerOnly exposure.");
        }
        var effectiveCurve = configuration.ScoreCurve ?? competitionConfiguration?.DefaultScoreCurve;
        if (effectiveCurve is not null && configuration.ScoreCurve is null)
            errors.AddRange(ScoreCurve.Validate(effectiveCurve, eligibleTeamCount));
        if (effectiveCurve is not null)
            ValidateNextPrice(effectiveCurve,
                configuration.ScoreSettlementMode ?? competitionConfiguration?.ScoreSettlementMode
                    ?? NoCTF.Domain.Competitions.CtfScoreSettlementMode.DynamicRecalculation,
                eligibleTeamCount, errors);
        return errors;
    }

    private static void ValidateNextPrice(ScoreCurveConfiguration curve,
        NoCTF.Domain.Competitions.CtfScoreSettlementMode settlement, int eligibleTeamCount, List<string> errors)
    {
        if (settlement != NoCTF.Domain.Competitions.CtfScoreSettlementMode.AtSolve
            || curve.DecayMode != ScoreDecayMode.Custom || errors.Count != 0)
            return;
        try
        {
            _ = ScoreCurve.Evaluate(curve, checked(eligibleTeamCount + 1), eligibleTeamCount);
        }
        catch (Exception exception) when (ScoreCurveExpression.IsValidationException(exception))
        {
            errors.Add($"CustomExpression is invalid for the next-solve price: {exception.Message}");
        }
    }

    private static void ValidateMaxAttempts(int? maxAttempts, ICollection<string> errors)
    {
        if (maxAttempts is <= 0)
            errors.Add("MaxFlagAttempts must be positive when configured.");
    }

    private static void ValidateFlagTemplate(
        PerTeamFlagTemplate? template,
        ICollection<string> errors)
    {
        if (template is not null && !PerTeamFlagGenerator.IsValidTemplate(template))
            errors.Add("FlagTemplate is invalid.");
    }

}
