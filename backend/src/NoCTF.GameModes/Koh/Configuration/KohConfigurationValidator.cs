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

    public static IReadOnlyList<string> Validate(KohChallengeConfiguration configuration)
    {
        var errors = new List<string>();
        if (!Uri.TryCreate(configuration.AgentUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            errors.Add("AgentUrl must be an absolute HTTP or HTTPS URL.");
        errors.AddRange(Registration.ChallengeRuntimeTemplateValidator.Validate(configuration.Runtime));
        if (configuration.TeamIdentifiers is { } identifiers)
        {
            if (identifiers.Keys.Any(string.IsNullOrWhiteSpace))
                errors.Add("KoH team identifiers cannot be empty.");
            if (identifiers.Values.Any(teamId => teamId == Guid.Empty))
                errors.Add("KoH team identifiers must map to non-empty Team IDs.");
            if (identifiers.Values.Distinct().Count() != identifiers.Count)
                errors.Add("Each KoH Team ID can have only one external identifier.");
        }
        return errors;
    }
}
