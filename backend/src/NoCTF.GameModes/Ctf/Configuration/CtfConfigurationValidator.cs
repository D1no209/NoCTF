using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Scoring;
using NoCTF.GameModes.Flags;
using NoCTF.GameModes.Scoring;

namespace NoCTF.GameModes.Ctf.Configuration;

public static class CtfConfigurationValidator
{
    private static readonly ScoreCurveEvaluator ScoreCurve = new();

    public static IReadOnlyList<string> Validate(CtfConfiguration configuration) => Validate(configuration, 1);

    public static IReadOnlyList<string> Validate(CtfConfiguration configuration, int eligibleTeamCount)
    {
        var errors = ScoreCurve.Validate(configuration.DefaultScoreCurve, eligibleTeamCount).ToList();
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
        if (configuration.Runtime is { FlagSource: not RuntimeFlagSource.PerTeam })
        {
            errors.Add("CTF runtimes must use PerTeam flags injected into the runtime environment.");
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
        return errors;
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
