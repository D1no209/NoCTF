using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Ctf.Configuration;

namespace NoCTF.GameModes.PatchVerification.Configuration;

public sealed record PatchVerificationConfiguration(
    ChallengeRuntimeTemplate Runtime,
    string PatchEntrypoint,
    IReadOnlyList<string>? PatchCommand,
    int PatchTimeoutSeconds,
    RunnerJobConfiguration Checker,
    int ReadyTimeoutSeconds,
    long MaximumPatchUploadBytes,
    int MaximumAttempts,
    bool RequiresBreak,
    bool CheckerFixInput,
    bool CheckerAllowRoot);

public static class PatchVerificationConfigurationResolver
{
    public static PatchVerificationConfiguration? Resolve(
        GameMode mode,
        string competitionConfigurationJson,
        string challengeRulesJson,
        string challengeDefinitionJson) => mode switch
        {
            GameMode.Awdp => ResolveAwdp(
                competitionConfigurationJson,
                challengeRulesJson,
                challengeDefinitionJson),
            GameMode.Ctf => ResolveCtf(challengeRulesJson, challengeDefinitionJson),
            _ => null
        };

    private static PatchVerificationConfiguration? ResolveAwdp(
        string competitionConfigurationJson,
        string challengeRulesJson,
        string challengeDefinitionJson)
    {
        var configuration = AwdpConfigurationResolver.Resolve(
            competitionConfigurationJson,
            challengeRulesJson,
            challengeDefinitionJson);
        return configuration.Runtime is null || configuration.Checker is null
            ? null
            : new(
                configuration.Runtime,
                configuration.PatchEntrypoint,
                configuration.PatchCommand,
                configuration.PatchTimeoutSeconds,
                configuration.Checker,
                configuration.ReadyTimeoutSeconds,
                configuration.MaximumPatchUploadBytes,
                configuration.MaxFixSubmissions,
                configuration.RequireBreakBeforeFix,
                configuration.CheckerFixInput,
                configuration.CheckerAllowRoot);
    }

    private static PatchVerificationConfiguration? ResolveCtf(
        string challengeRulesJson,
        string challengeDefinitionJson)
    {
        var configuration = CtfPatchVerificationConfigurationResolver.Resolve(
            challengeDefinitionJson,
            challengeRulesJson);
        return configuration is null
            ? null
            : new(
                configuration.Runtime,
                configuration.PatchEntrypoint,
                configuration.PatchCommand,
                configuration.PatchTimeoutSeconds,
                configuration.Checker,
                configuration.ReadyTimeoutSeconds,
                configuration.MaximumPatchUploadBytes,
                configuration.MaxPatchAttempts,
                false,
                configuration.CheckerFixInput,
                configuration.CheckerAllowRoot);
    }
}
