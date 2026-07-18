namespace NoCTF.GameModes.Koh.Configuration;

public static class KohConfigurationValidator
{
    public static IReadOnlyList<string> Validate(KohConfiguration configuration)
    {
        var errors = new List<string>();
        if (configuration.PollIntervalSeconds <= 0) errors.Add("PollIntervalSeconds must be positive.");
        if (configuration.ControlPointsPerInterval < 0) errors.Add("ControlPointsPerInterval cannot be negative.");
        return errors;
    }
}
