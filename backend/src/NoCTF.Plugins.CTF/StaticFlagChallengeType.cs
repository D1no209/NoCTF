using NoCTF.PluginBase;

namespace NoCTF.Plugins.CTF;

/// <summary>
/// Challenge type for static flags — the flag is a fixed string stored in FlagSecret.
/// </summary>
public class StaticFlagChallengeType : IChallengeType
{
    public string TypeId => "ctf-static";

    public Task<ValidationResult> ValidateFlagAsync(
        string submittedFlag,
        ChallengeContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.FlagSecret))
            return Task.FromResult(ValidationResult.Invalid);

        var isMatch = FlagValidator.IsMatch(submittedFlag, context.FlagSecret);
        return Task.FromResult(isMatch ? ValidationResult.Valid : ValidationResult.Invalid);
    }

    public ContainerConfig? GetContainerConfig(ChallengeContext context) => null;
}
