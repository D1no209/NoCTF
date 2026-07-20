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
        return errors;
    }
}
