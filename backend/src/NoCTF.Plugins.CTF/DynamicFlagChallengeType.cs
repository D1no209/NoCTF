using NoCTF.PluginBase;

namespace NoCTF.Plugins.CTF;

/// <summary>
/// Challenge type for dynamic/per-team flags — the flag is derived from FlagSecret + team context.
/// For MVP, this reuses the same static comparison logic. The FlagSecret can be a template
/// that gets resolved per-team by the challenge setup process.
/// </summary>
public class DynamicFlagChallengeType : IChallengeType
{
    public string TypeId => "ctf-dynamic";

    public Task<ValidationResult> ValidateFlagAsync(
        string submittedFlag,
        ChallengeContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.FlagSecret))
            return Task.FromResult(ValidationResult.Invalid);

        // Dynamic flags: FlagSecret stores the expected flag for this team's context.
        // The caller is responsible for providing the correct per-team FlagSecret.
        var isMatch = FlagValidator.IsMatch(submittedFlag, context.FlagSecret);
        return Task.FromResult(isMatch ? ValidationResult.Valid : ValidationResult.Invalid);
    }

    public ContainerConfig? GetContainerConfig(ChallengeContext context)
    {
        // Container config is provided via challenge configuration if needed
        if (!context.Configuration.TryGetValue("image", out var image) || string.IsNullOrEmpty(image))
            return null;

        context.Configuration.TryGetValue("command", out var command);

        return new ContainerConfig(
            Image: image,
            Command: string.IsNullOrEmpty(command) ? null : command
        );
    }
}
