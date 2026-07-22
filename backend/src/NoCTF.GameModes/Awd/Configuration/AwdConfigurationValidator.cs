namespace NoCTF.GameModes.Awd.Configuration;

public static class AwdConfigurationValidator
{
    public static IReadOnlyList<string> Validate(AwdConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.RoundDurationSeconds <= 0) errors.Add("RoundDurationSeconds must be positive.");
        if (configuration.TotalRounds <= 0) errors.Add("TotalRounds must be positive.");
        if (configuration.FlagValidityRounds <= 0) errors.Add("FlagValidityRounds must be positive.");
        if (configuration.AttackPoints < 0 || configuration.ServiceOnlinePoints < 0
            || configuration.ServiceDownPenalty < 0 || configuration.VictimPenalty < 0)
            errors.Add("Scoring values cannot be negative.");
        return errors;
    }

    public static IReadOnlyList<string> Validate(AwdChallengeConfiguration configuration)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(configuration.FlagFormat)) errors.Add("FlagFormat is required.");
        else if (configuration.FlagFormat.Length > 256) errors.Add("FlagFormat cannot exceed 256 characters.");
        if (configuration.MaxFlagAttempts is <= 0) errors.Add("MaxFlagAttempts must be positive when configured.");
        errors.AddRange(Registration.ChallengeRuntimeTemplateValidator.Validate(configuration.Runtime));
        errors.AddRange(Registration.RunnerJobConfigurationValidator.Validate(configuration.Checker, "Checker"));
        if (configuration.Runtime is not null && configuration.FlagInjection is null)
            errors.Add("FlagInjection is required when Runtime is configured.");
        if (configuration.FlagInjection is { } injection)
        {
            if (injection.Command.Count == 0 || injection.Command.Any(string.IsNullOrWhiteSpace))
                errors.Add("FlagInjection.Command must contain non-empty arguments.");
            if (injection.TimeoutSeconds <= 0)
                errors.Add("FlagInjection.TimeoutSeconds must be positive.");
        }
        return errors;
    }
}
