namespace NoCTF.GameModes.Ctf.Configuration;

public static class CtfConfigurationValidator
{
    public static IReadOnlyList<string> Validate(CtfConfiguration configuration)
    {
        var errors = ValidatePoints(configuration.DefaultPoints).ToList();
        if (configuration.BloodRewards.Count > 3)
            errors.Add("At most three blood rewards are supported.");
        foreach (var reward in configuration.BloodRewards)
        {
            if (reward.Value < 0)
                errors.Add("Blood reward values cannot be negative.");
            if (reward.Policy != BloodRewardPolicy.FixedPoints && reward.Value > 100)
                errors.Add("Blood reward percentages cannot exceed 100.");
        }
        return errors;
    }

    public static IReadOnlyList<string> Validate(CtfChallengeConfiguration configuration)
    {
        var errors = configuration.Points is null
            ? []
            : ValidatePoints(configuration.Points).ToList();
        if (configuration.BloodRewards is null)
        {
            ValidateMaxAttempts(configuration.MaxFlagAttempts, errors);
            return errors;
        }
        if (configuration.BloodRewards.Count > 3)
            errors.Add("At most three blood rewards are supported.");
        foreach (var reward in configuration.BloodRewards)
        {
            if (reward.Value < 0)
                errors.Add("Blood reward values cannot be negative.");
            if (reward.Policy != BloodRewardPolicy.FixedPoints && reward.Value > 100)
                errors.Add("Blood reward percentages cannot exceed 100.");
        }
        ValidateMaxAttempts(configuration.MaxFlagAttempts, errors);
        return errors;
    }

    private static void ValidateMaxAttempts(int? maxAttempts, ICollection<string> errors)
    {
        if (maxAttempts is <= 0)
            errors.Add("MaxFlagAttempts must be positive when configured.");
    }

    public static IReadOnlyList<string> ValidatePoints(CtfPointConfiguration points)
    {
        var errors = new List<string>();
        if (points.InitialPoints <= 0) errors.Add("InitialPoints must be positive.");
        if (points.MinimumPoints < 0 || points.MinimumPoints > points.InitialPoints)
            errors.Add("MinimumPoints must be between zero and InitialPoints.");
        if (points.DecayFactor <= 0) errors.Add("DecayFactor must be positive.");
        return errors;
    }
}
