namespace NoCTF.GameModes.Awd.Configuration;

using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Scoring;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Flags;

public static class AwdConfigurationValidator
{
    public static IReadOnlyList<string> Validate(AwdConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.HardeningDurationSeconds < 0) errors.Add("HardeningDurationSeconds cannot be negative.");
        if (configuration.RoundDurationSeconds <= 0) errors.Add("RoundDurationSeconds must be positive.");
        if (configuration.CheckerIntervalSeconds <= 0) errors.Add("CheckerIntervalSeconds must be positive.");
        if (configuration.AttackPoints < 0 || configuration.VictimDefensePoolPoints < 0
            || configuration.ServiceHealthyPoints < 0 || configuration.ServiceUnhealthyPenalty < 0)
            errors.Add("Scoring values cannot be negative.");
        if (configuration.AttackPoints > ScoreValueLimits.MaximumConfiguredValue
            || configuration.VictimDefensePoolPoints > ScoreValueLimits.MaximumConfiguredValue
            || configuration.ServiceHealthyPoints > ScoreValueLimits.MaximumConfiguredValue
            || configuration.ServiceUnhealthyPenalty > ScoreValueLimits.MaximumConfiguredValue)
            errors.Add($"Scoring values cannot exceed {ScoreValueLimits.MaximumConfiguredValue}.");
        if (configuration.FlagTemplate is { } flagTemplate
            && !PerTeamFlagGenerator.IsValidTemplate(flagTemplate))
            errors.Add("FlagTemplate is invalid.");
        return errors;
    }

    public static IReadOnlyList<string> Validate(AwdChallengeConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.AttackPoints < 0 || configuration.VictimDefensePoolPoints < 0
            || configuration.ServiceHealthyPoints < 0 || configuration.ServiceUnhealthyPenalty < 0)
            errors.Add("Scoring overrides cannot be negative.");
        if (configuration.AttackPoints > ScoreValueLimits.MaximumConfiguredValue
            || configuration.VictimDefensePoolPoints > ScoreValueLimits.MaximumConfiguredValue
            || configuration.ServiceHealthyPoints > ScoreValueLimits.MaximumConfiguredValue
            || configuration.ServiceUnhealthyPenalty > ScoreValueLimits.MaximumConfiguredValue)
            errors.Add($"Scoring overrides cannot exceed {ScoreValueLimits.MaximumConfiguredValue}.");
        if (configuration.CheckerIntervalSeconds is <= 0)
            errors.Add("CheckerIntervalSeconds must be positive when configured.");
        if (configuration.FlagTemplate is { } flagTemplate
            && !PerTeamFlagGenerator.IsValidTemplate(flagTemplate))
            errors.Add("FlagTemplate is invalid.");
        errors.AddRange(Registration.ChallengeRuntimeTemplateValidator.Validate(
            configuration.Runtime));
        if (configuration.Runtime is { Allocation: not RuntimeAllocation.PerTeam })
            errors.Add("AWD runtimes must use PerTeam allocation.");
        if (configuration.Runtime is { FlagSource: not RuntimeFlagSource.AwdRotation })
            errors.Add("AWD runtimes must use AwdRotation flags.");
        if (configuration.Runtime?.Definition is not null
            and not (ContainerRuntimeDefinition or ComposeRuntimeDefinition))
            errors.Add("AWD runtimes only support Container or Compose.");
        if (configuration.Runtime is not null
            && !(configuration.Runtime.UrlBindings ?? []).Any(binding =>
                binding is not null
                && binding.Exposure == RuntimeExposure.Participants))
        {
            errors.Add("AWD runtimes require at least one Participants access URL.");
        }
        errors.AddRange(Registration.RunnerJobConfigurationValidator.Validate(
            configuration.Checker?.Job,
            "Checker"));
        if (configuration.Checker is { } checker)
            ValidateChecker(configuration.Runtime, checker, errors);
        if (configuration.CheckerAllowRoot && configuration.Checker is null)
            errors.Add("CheckerAllowRoot requires Checker.");
        if (configuration.Runtime is not null && configuration.FlagInjection is null)
            errors.Add("FlagInjection is required when Runtime is configured.");
        if (configuration.FlagInjection is { } injection)
        {
            if (string.IsNullOrWhiteSpace(injection.Command)
                || !injection.Command.Contains("${FLAG}", StringComparison.Ordinal))
                errors.Add("FlagInjection.Command must be a non-empty raw template containing ${FLAG}.");
            if (injection.TimeoutSeconds is <= 0
                or > AwdFlagInjectionExecutionBudget.MaximumCommandTimeoutSeconds)
            {
                errors.Add(
                    $"FlagInjection.TimeoutSeconds must be between 1 and {AwdFlagInjectionExecutionBudget.MaximumCommandTimeoutSeconds}.");
            }
            if (configuration.Runtime?.Definition is ComposeRuntimeDefinition
                && string.IsNullOrWhiteSpace(injection.ServiceName))
                errors.Add("FlagInjection.ServiceName is required for Compose runtimes.");
        }
        return errors;
    }

    private static void ValidateChecker(
        ChallengeRuntimeTemplate? runtime,
        AwdCheckerConfiguration checker,
        List<string> errors)
    {
        if (checker.Job is null)
            return;
        if (runtime is null)
        {
            errors.Add("Runtime is required when Checker is configured.");
            return;
        }
        switch (runtime.Definition)
        {
            case ContainerRuntimeDefinition when checker.TargetServiceName is null:
                break;
            case ComposeRuntimeDefinition when !string.IsNullOrWhiteSpace(checker.TargetServiceName):
                break;
            default:
                errors.Add("Checker.TargetServiceName is required only for Compose Runtime.");
                break;
        }
    }
}
