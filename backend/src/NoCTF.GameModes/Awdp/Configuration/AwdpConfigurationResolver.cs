using NoCTF.GameModes.Flags;
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;

namespace NoCTF.GameModes.Awdp.Configuration;

public static class AwdpConfigurationResolver
{
    private const string DefaultPatchEntrypoint = "fix.sh";

    public static AwdpEffectiveConfiguration Resolve(
        AwdpConfiguration competition,
        AwdpChallengeConfiguration rules,
        AwdpChallengeConfiguration definition) =>
        new(
            competition.RoundDurationSeconds,
            rules.Break ?? competition.Break,
            rules.Fix ?? competition.Fix,
            rules.FlagWrongPenalty ?? competition.FlagWrongPenalty,
            rules.ExploitSucceededPenalty ?? competition.ExploitSucceededPenalty,
            rules.ServiceAbnormalPenalty ?? competition.ServiceAbnormalPenalty,
            rules.RequireBreakBeforeFix ?? competition.RequireBreakBeforeFix,
            rules.MaxBreakSubmissions ?? competition.MaxBreakSubmissions,
            rules.MaxFixSubmissions ?? competition.MaxFixSubmissions,
            rules.EvaluationDispatchMode ?? competition.EvaluationDispatchMode,
            definition.Runtime,
            definition.PatchEntrypoint ?? DefaultPatchEntrypoint,
            definition.PatchCommand,
            definition.PatchTimeoutSeconds ?? AwdpFixExecutionBudget.DefaultPatchTimeoutSeconds,
            definition.Checker,
            definition.ReadyTimeoutSeconds ?? AwdpFixExecutionBudget.DefaultReadyTimeoutSeconds,
            definition.MaximumPatchUploadBytes
                ?? NoCTF.Application.GameplayFacts.PatchUploads.PatchUploadRules.DefaultMaximumArchiveBytes,
            rules.FlagTemplate ?? competition.FlagTemplate ?? PerTeamFlagTemplate.Default,
            definition.CheckerFixInput,
            definition.CheckerAllowRoot);

    public static AwdpEffectiveConfiguration Resolve(
        AwdpCompetitionModeConfiguration competition,
        AwdpCompetitionChallengeRules rules,
        AwdpChallengeDefinition definition) => Resolve(
        TypedGameModeConfiguration.Awdp(competition),
        TypedGameModeConfiguration.Awdp(rules),
        TypedGameModeConfiguration.Awdp(definition));
}
