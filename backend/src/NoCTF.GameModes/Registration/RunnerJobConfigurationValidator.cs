using NoCTF.Application.Runtime.Configuration;

namespace NoCTF.GameModes.Registration;

public static class RunnerJobConfigurationValidator
{
    public static IReadOnlyList<string> Validate(RunnerJobConfiguration? configuration, string name)
    {
        if (configuration is null) return [];
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(configuration.Image)) errors.Add($"{name}.Image is required.");
        else if (configuration.Image.Length > 512)
            errors.Add($"{name}.Image cannot exceed 512 characters.");
        if (configuration.Command?.Any(string.IsNullOrWhiteSpace) == true)
            errors.Add($"{name}.Command cannot contain blank arguments.");
        if (configuration.TimeoutSeconds is < 1 or > 1800)
            errors.Add($"{name}.TimeoutSeconds must be between 1 and 1800.");
        var hasReservedEnvironmentVariable = false;
        foreach (var variable in configuration.Environment ?? new Dictionary<string, string>())
        {
            if (!IsEnvironmentVariableName(variable.Key))
                errors.Add($"{name} environment variable '{variable.Key}' is invalid.");
            if (variable.Key.StartsWith("NOCTF_", StringComparison.OrdinalIgnoreCase))
                hasReservedEnvironmentVariable = true;
        }
        if (hasReservedEnvironmentVariable)
            errors.Add($"{name} environment variables cannot use the NOCTF_ prefix.");
        return errors;
    }

    private static bool IsEnvironmentVariableName(string name)
    {
        if (string.IsNullOrEmpty(name)
            || !(IsAsciiLetter(name[0]) || name[0] == '_'))
            return false;
        return name.Skip(1).All(character =>
            IsAsciiLetter(character) || char.IsAsciiDigit(character) || character == '_');
    }

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}
