using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.GameplayFacts.PatchVerification;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;

namespace NoCTF.GameModes.Ctf.Configuration;

public sealed record CtfPatchVerificationConfiguration(
    ChallengeRuntimeTemplate Runtime,
    string PatchEntrypoint,
    IReadOnlyList<string>? PatchCommand,
    int PatchTimeoutSeconds,
    RunnerJobConfiguration Checker,
    int ReadyTimeoutSeconds,
    long MaximumPatchUploadBytes,
    int MaxPatchAttempts,
    bool CheckerFixInput,
    bool CheckerAllowRoot);

public static class CtfPatchVerificationConfigurationResolver
{
    public const int DefaultMaxPatchAttempts = 10;
    public const long DefaultMaximumPatchUploadBytes = 64L * 1024 * 1024;
    private const string DefaultPatchEntrypoint = "fix.sh";

    public static CtfPatchVerificationConfiguration? Resolve(string definitionJson, string rulesJson)
    {
        var definition = CtfConfigurationParser.ParseDefinition(definitionJson);
        if (definition.InteractionKind != CtfInteractionKind.PatchVerification
            || definition.Runtime is null
            || definition.Checker is null)
        {
            return null;
        }

        var rules = CtfConfigurationParser.ParseRules(rulesJson);
        return new(
            definition.Runtime,
            definition.PatchEntrypoint ?? DefaultPatchEntrypoint,
            definition.PatchCommand,
            definition.PatchTimeoutSeconds
                ?? PatchVerificationExecutionBudget.DefaultPatchTimeoutSeconds,
            definition.Checker,
            definition.ReadyTimeoutSeconds
                ?? PatchVerificationExecutionBudget.DefaultReadyTimeoutSeconds,
            definition.MaximumPatchUploadBytes
                ?? DefaultMaximumPatchUploadBytes,
            rules.MaxPatchAttempts ?? DefaultMaxPatchAttempts,
            definition.CheckerFixInput,
            definition.CheckerAllowRoot);
    }
}
