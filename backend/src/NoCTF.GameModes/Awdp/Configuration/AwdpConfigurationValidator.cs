namespace NoCTF.GameModes.Awdp.Configuration;

public static class AwdpConfigurationValidator
{
    public static IReadOnlyList<string> Validate(AwdpConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.RoundDurationSeconds <= 0) errors.Add("RoundDurationSeconds must be positive.");
        if (configuration.Break.Points < 0 || configuration.Fix.Points < 0)
            errors.Add("Achievement points cannot be negative.");
        if (configuration.ViolationPenalty < 0 || configuration.ServiceDownPenalty < 0)
            errors.Add("Penalty values cannot be negative.");
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
        if (string.IsNullOrWhiteSpace(configuration.PatchEntrypoint)) errors.Add("PatchEntrypoint is required.");
        else if (configuration.PatchEntrypoint.Length > 256) errors.Add("PatchEntrypoint cannot exceed 256 characters.");
        else if (Path.IsPathRooted(configuration.PatchEntrypoint)
                 || configuration.PatchEntrypoint.Split('/', '\\').Any(segment => segment is ".." or "."))
            errors.Add("PatchEntrypoint must be a safe relative path.");
        if (configuration.PatchTimeoutSeconds <= 0) errors.Add("PatchTimeoutSeconds must be positive.");
        if (configuration.ReadyTimeoutSeconds <= 0) errors.Add("ReadyTimeoutSeconds must be positive.");
        if (configuration.Fix is not null && configuration.Runtime is null) errors.Add("Runtime is required when Fix is enabled.");
        if (configuration.Fix is not null && configuration.Checker is null) errors.Add("Checker is required when Fix is enabled.");
        if (configuration.Fix is not null && configuration.TargetPort is < 1 or > 65535) errors.Add("TargetPort must be a valid TCP port when Fix is enabled.");
        if (configuration.Runtime is not null && configuration.Checker is not null
            && configuration.Runtime.Provider != configuration.Checker.Provider)
            errors.Add("Runtime and Checker must use the same provider.");
        errors.AddRange(Registration.ChallengeRuntimeTemplateValidator.Validate(configuration.Runtime));
        errors.AddRange(Registration.RunnerJobConfigurationValidator.Validate(configuration.Checker, "Checker"));
        return errors;
    }
}
