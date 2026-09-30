using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.GameplayFacts.PatchVerification;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.GameModes.Registration;

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
    bool CheckerFixInput);

public static class CtfPatchVerificationConfigurationResolver
{
    public const int DefaultMaxPatchAttempts = 10;
    public const long DefaultMaximumPatchUploadBytes = 64L * 1024 * 1024;
    private const string DefaultPatchEntrypoint = "fix.sh";

    public static CtfPatchVerificationConfiguration? Resolve(
        CtfChallengeDefinition definition,
        CtfCompetitionChallengeRules rules)
    {
        if (definition.InteractionKind != CtfInteractionKind.PatchVerification)
            return null;
        var runtime = TypedGameModeConfiguration.Runtime(definition.Runtime);
        var checker = TypedGameModeConfiguration.Checker(definition);
        if (runtime is null || checker is null) return null;
        return new(
            runtime,
            definition.PatchEntrypoint ?? DefaultPatchEntrypoint,
            definition.StringItems
                .Where(item => item.Kind == ChallengeDefinitionStringKind.PatchCommand)
                .OrderBy(item => item.Position)
                .Select(item => item.Value).ToArray(),
            definition.PatchTimeoutSeconds
                ?? PatchVerificationExecutionBudget.DefaultPatchTimeoutSeconds,
            checker,
            definition.ReadyTimeoutSeconds
                ?? PatchVerificationExecutionBudget.DefaultReadyTimeoutSeconds,
            definition.MaximumPatchUploadBytes ?? DefaultMaximumPatchUploadBytes,
            rules.MaxPatchAttempts ?? DefaultMaxPatchAttempts,
            definition.CheckerFixInput);
    }
}
