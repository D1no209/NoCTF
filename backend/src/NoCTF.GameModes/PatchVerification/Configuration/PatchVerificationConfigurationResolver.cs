using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
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
    bool CheckerFixInput);

public static class PatchVerificationConfigurationResolver
{
    public static PatchVerificationConfiguration? Resolve(
        GameMode mode,
        CompetitionModeConfiguration competitionConfiguration,
        CompetitionChallengeRules rules,
        ChallengeDefinition definition) => (mode, competitionConfiguration, rules, definition) switch
        {
            (GameMode.Awdp, AwdpCompetitionModeConfiguration competition,
                AwdpCompetitionChallengeRules awdpRules,
                AwdpChallengeDefinition awdpDefinition) =>
                ResolveAwdp(competition, awdpRules, awdpDefinition),
            (GameMode.Ctf, CtfCompetitionModeConfiguration,
                CtfCompetitionChallengeRules ctfRules,
                CtfChallengeDefinition ctfDefinition) =>
                ResolveCtf(ctfRules, ctfDefinition),
            _ => null
        };

    private static PatchVerificationConfiguration? ResolveAwdp(
        AwdpCompetitionModeConfiguration competition,
        AwdpCompetitionChallengeRules rules,
        AwdpChallengeDefinition definition)
    {
        var configuration = AwdpConfigurationResolver.Resolve(competition, rules, definition);
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
                configuration.CheckerFixInput);
    }

    private static PatchVerificationConfiguration? ResolveCtf(
        CtfCompetitionChallengeRules rules,
        CtfChallengeDefinition definition)
    {
        var configuration = CtfPatchVerificationConfigurationResolver.Resolve(definition, rules);
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
                configuration.CheckerFixInput);
    }

}
