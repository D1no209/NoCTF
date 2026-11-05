using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.Domain.LiveSolo;
using NoCTF.GameModes.Registration;

namespace NoCTF.GameModes.Challenges.Capabilities;

public sealed record FixSubmissionConfiguration(
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

public static class FixSubmissionConfigurationResolver
{
    public static FixSubmissionConfiguration? Resolve(
        GameMode mode,
        CompetitionModeConfiguration competitionConfiguration,
        CompetitionChallengeRules rules,
        ChallengeDefinition definition) => (mode, competitionConfiguration, rules, definition) switch
        {
            (GameMode.Awdp, AwdpCompetitionModeConfiguration competition,
                AwdpCompetitionChallengeRules awdpRules,
                ChallengeDefinition awdpDefinition) =>
                ResolveAwdp(competition, awdpRules, awdpDefinition),
            (GameMode.LiveSolo, LiveSoloCompetitionModeConfiguration,
                LiveSoloCompetitionChallengeRules { CompletionKind: LiveSoloCompletionKind.FixAndCheck },
                ChallengeDefinition scopedDefinition) => ResolveCapabilities(scopedDefinition, rules),
            (GameMode.Awd, AwdCompetitionModeConfiguration,
                AwdCompetitionChallengeRules { AllowFixSubmissions: true },
                ChallengeDefinition persistentDefinition) => ResolveCapabilities(persistentDefinition, rules),
            _ => null
        };

    private static FixSubmissionConfiguration? ResolveAwdp(
        AwdpCompetitionModeConfiguration competition,
        AwdpCompetitionChallengeRules rules,
        ChallengeDefinition definition)
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

    private static FixSubmissionConfiguration? ResolveCapabilities(ChallengeDefinition definition, CompetitionChallengeRules rules)
    {
        if (definition.Fix is not { } fix || definition.Check is not { } check
            || TypedGameModeConfiguration.Runtime(definition, rules) is not { } runtime
            || TypedGameModeConfiguration.Checker(definition) is not { } checker) return null;
        return new(runtime, fix.Entrypoint, fix.Command.OrderBy(item => item.Position).Select(item => item.Value).ToArray(),
            fix.TimeoutSeconds, checker, check.ReadyTimeoutSeconds, fix.MaximumUploadBytes, int.MaxValue, false, check.AcceptsFixInput);
    }

}
