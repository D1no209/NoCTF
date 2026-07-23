namespace NoCTF.GameModes.Awd.Configuration;

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
        if (configuration.CheckerIntervalSeconds is <= 0)
            errors.Add("CheckerIntervalSeconds must be positive when configured.");
        if (configuration.FlagTemplate is { } flagTemplate
            && !PerTeamFlagGenerator.IsValidTemplate(flagTemplate))
            errors.Add("FlagTemplate is invalid.");
        errors.AddRange(Registration.ChallengeRuntimeTemplateValidator.Validate(configuration.Runtime));
        errors.AddRange(Registration.RunnerJobConfigurationValidator.Validate(configuration.Checker, "Checker"));
        if (configuration.Runtime is not null && configuration.FlagInjection is null)
            errors.Add("FlagInjection is required when Runtime is configured.");
        if (configuration.FlagInjection is { } injection)
        {
            if (string.IsNullOrWhiteSpace(injection.Command)
                || !injection.Command.Contains("${FLAG}", StringComparison.Ordinal))
                errors.Add("FlagInjection.Command must be a non-empty raw template containing ${FLAG}.");
            if (injection.TimeoutSeconds <= 0)
                errors.Add("FlagInjection.TimeoutSeconds must be positive.");
            if (configuration.Runtime?.RuntimeKind == NoCTF.Domain.Runtime.RuntimeKind.Compose
                && string.IsNullOrWhiteSpace(injection.ServiceName))
                errors.Add("FlagInjection.ServiceName is required for Compose runtimes.");
        }
        return errors;
    }
}
