using NoCTF.GameModes.Ctf.Scoring;
using DynamicExpresso.Exceptions;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Scoring;
using NoCTF.GameModes.Flags;

namespace NoCTF.GameModes.Ctf.Configuration;

public static class CtfConfigurationValidator
{
    private static readonly CtfScoreExpression ScoreExpression = new();

    public static IReadOnlyList<string> Validate(CtfConfiguration configuration) => Validate(configuration, 1);

    public static IReadOnlyList<string> Validate(CtfConfiguration configuration, int eligibleTeamCount)
    {
        var errors = ValidatePoints(configuration.DefaultPoints).ToList();
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
        ValidateExpression(configuration.ScoreExpression, configuration.DefaultPoints, eligibleTeamCount, errors);
        return errors;
    }

    public static IReadOnlyList<string> Validate(CtfChallengeConfiguration configuration) =>
        Validate(configuration, null, 1);

    public static IReadOnlyList<string> Validate(
        CtfChallengeConfiguration configuration,
        CtfConfiguration? competitionConfiguration,
        int eligibleTeamCount)
    {
        var errors = configuration.Points is null
            ? []
            : ValidatePoints(configuration.Points).ToList();
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
        if (configuration.FlagTemplate is not null
            && configuration.Runtime?.FlagSource != RuntimeFlagSource.PerTeam)
        {
            errors.Add("FlagTemplate requires a PerTeam runtime FlagSource.");
        }
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
        var effectivePoints = configuration.Points ?? competitionConfiguration?.DefaultPoints;
        var effectiveExpression = configuration.ScoreExpression ?? competitionConfiguration?.ScoreExpression;
        if (effectivePoints is not null)
            ValidateExpression(effectiveExpression, effectivePoints, eligibleTeamCount, errors);
        else
            ValidateExpressionSyntax(effectiveExpression, errors);
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

    public static IReadOnlyList<string> ValidatePoints(CtfPointConfiguration points)
    {
        var errors = new List<string>();
        if (points.InitialPoints <= 0) errors.Add("InitialPoints must be positive.");
        if (points.InitialPoints > ScoreValueLimits.MaximumConfiguredValue)
            errors.Add($"InitialPoints cannot exceed {ScoreValueLimits.MaximumConfiguredValue}.");
        if (points.MinimumPoints > ScoreValueLimits.MaximumConfiguredValue)
            errors.Add($"MinimumPoints cannot exceed {ScoreValueLimits.MaximumConfiguredValue}.");
        if (points.MinimumPoints < 0 || points.MinimumPoints > points.InitialPoints)
            errors.Add("MinimumPoints must be between zero and InitialPoints.");
        if (points.DecayFactor <= 1) errors.Add("DecayFactor must be greater than one.");
        return errors;
    }

    private static void ValidateExpression(
        string? expression,
        CtfPointConfiguration points,
        int eligibleTeamCount,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return;
        try
        {
            ScoreExpression.Validate(expression, points.InitialPoints, points.MinimumPoints,
                points.DecayFactor, eligibleTeamCount);
        }
        catch (Exception exception) when (exception is DynamicExpressoException or ArgumentException
            or InvalidOperationException or OverflowException or DivideByZeroException)
        {
            errors.Add($"ScoreExpression is invalid: {exception.Message}");
        }
    }

    private static void ValidateExpressionSyntax(string? expression, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return;
        try
        {
            ScoreExpression.ValidateSyntax(expression);
        }
        catch (Exception exception) when (exception is DynamicExpressoException or ArgumentException or InvalidOperationException)
        {
            errors.Add($"ScoreExpression is invalid: {exception.Message}");
        }
    }
}
