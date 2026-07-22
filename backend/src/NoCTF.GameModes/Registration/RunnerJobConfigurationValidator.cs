using NoCTF.Application.Runtime.Ports;

namespace NoCTF.GameModes.Registration;

public static class RunnerJobConfigurationValidator
{
    public static IReadOnlyList<string> Validate(RunnerJobConfiguration? configuration, string name)
    {
        if (configuration is null) return [];
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(configuration.Image)) errors.Add($"{name}.Image is required.");
        if (configuration.Image.Length > 512) errors.Add($"{name}.Image cannot exceed 512 characters.");
        if (configuration.Command?.Any(string.IsNullOrWhiteSpace) == true)
            errors.Add($"{name}.Command cannot contain blank arguments.");
        if (configuration.TimeoutSeconds is < 1 or > 1800)
            errors.Add($"{name}.TimeoutSeconds must be between 1 and 1800.");
        return errors;
    }
}
