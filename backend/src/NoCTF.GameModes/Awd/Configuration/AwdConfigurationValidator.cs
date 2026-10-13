namespace NoCTF.GameModes.Awd.Configuration;

using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Scoring;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Competitions.Awd;
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
            && !Flags.AwdFlagGenerator.IsValidTemplate(flagTemplate))
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
            && !Flags.AwdFlagGenerator.IsValidTemplate(flagTemplate))
            errors.Add("FlagTemplate is invalid.");
        errors.AddRange(Registration.ChallengeRuntimeTemplateValidator.Validate(
            configuration.Runtime));
        if (configuration.Runtime is { Allocation: not RuntimeAllocation.PerTeam })
            errors.Add("AWD runtimes must use PerTeam allocation.");
        if (configuration.Runtime is { FlagSource: not RuntimeFlagSource.AwdRotation })
            errors.Add("AWD runtimes must use AwdRotation flags.");
        if (configuration.Runtime?.Definition is not null
            and not (ContainerRuntimeDefinition))
            errors.Add("AWD runtimes only support Container.");
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
        if (configuration.Runtime is not null && configuration.FlagInjection is null && configuration.FlagStores is not { Count: > 0 })
            errors.Add("FlagInjection is required when Runtime is configured.");
        ValidateStores(configuration, errors);
        if (configuration.FlagInjection is { } injection && configuration.FlagStores is not { Count: > 0 })
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
            if (configuration.Runtime?.Definition is ContainerRuntimeDefinition
                && string.IsNullOrWhiteSpace(injection.ServiceName))
                errors.Add("FlagInjection.ServiceName is required for container runtimes.");
            if (configuration.Runtime?.Definition is ContainerRuntimeDefinition services && !services.Services.Any(service => service.Name == injection.ServiceName))
                errors.Add("FlagInjection.ServiceName must reference an existing service.");
        }
        return errors;
    }

    private static void ValidateStores(AwdChallengeConfiguration configuration, List<string> errors)
    {
        if (configuration.FlagStores is not { Count: > 0 } stores) return;
        if (stores.Count > 64 || stores.Select(item => item.Id).Distinct().Count() != stores.Count
            || stores.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != stores.Count)
            errors.Add("AWD requires at most 64 stores with unique ids and names.");
        foreach (var store in stores)
        {
            if (store.Id == Guid.Empty || string.IsNullOrWhiteSpace(store.Name) || store.Name.Length > 64
                || store.Name != store.Name.Trim() || !Enum.IsDefined(store.DeliveryMode)
                || store.InjectionTimeoutSeconds is <= 0 or > AwdFlagInjectionExecutionBudget.MaximumCommandTimeoutSeconds)
                errors.Add("AWD store identity, delivery mode or timeout is invalid.");
            if (store.TargetServiceName is { Length: > 64 } || store.InjectionCommand is { Length: > 8192 })
                errors.Add("AWD store service name or injection command exceeds its maximum length.");
            if (store.DeliveryMode == AwdFlagDeliveryMode.BusinessChecker)
            {
                if (configuration.Checker is null) errors.Add("Business-checker flag delivery requires a Checker.");
                if (!string.IsNullOrWhiteSpace(store.InjectionCommand)) errors.Add("Business-checker stores cannot configure an injection command.");
            }
            else if (store.DeliveryMode == AwdFlagDeliveryMode.RunnerCommand && configuration.Runtime is not null)
            {
                if (string.IsNullOrWhiteSpace(store.InjectionCommand) || !store.InjectionCommand.Contains("${FLAG}", StringComparison.Ordinal))
                    errors.Add("AWD store injection commands must contain ${FLAG}.");
                if (configuration.Runtime?.Definition is not ContainerRuntimeDefinition container
                    || !container.Services.Any(service => service.Name == store.TargetServiceName))
                    errors.Add("AWD store injection must reference an existing container service.");
            }
        }
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
        if (runtime.Definition is not ContainerRuntimeDefinition container
            || !container.Services.Any(service => service.Name == checker.TargetServiceName))
            errors.Add("Checker.TargetServiceName must reference an existing Runtime service.");
    }
}
