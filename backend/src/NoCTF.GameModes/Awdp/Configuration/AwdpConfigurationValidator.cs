namespace NoCTF.GameModes.Awdp.Configuration;

public static class AwdpConfigurationValidator
{
    public static IReadOnlyList<string> Validate(AwdpConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.RoundDurationSeconds <= 0) errors.Add("RoundDurationSeconds must be positive.");
        if (configuration.Break.Points < 0 || configuration.Fix.Points < 0)
            errors.Add("Achievement points cannot be negative.");
        return errors;
    }

    public static IReadOnlyList<string> Validate(AwdpChallengeConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.Break is null && configuration.Fix is null)
            errors.Add("At least one of Break or Fix must be configured.");
        if (configuration.Break?.Points < 0 || configuration.Fix?.Points < 0)
            errors.Add("Achievement points cannot be negative.");
        if (configuration.MaxBreakAttempts < 1) errors.Add("MaxBreakAttempts must be at least one.");
        if (configuration.MaxFixAttempts < 1) errors.Add("MaxFixAttempts must be at least one.");
        errors.AddRange(Registration.ChallengeRuntimeTemplateValidator.Validate(configuration.Runtime));
        return errors;
    }
}
