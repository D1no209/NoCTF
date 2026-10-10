using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Flags;

namespace NoCTF.GameModes.Registration;

/// <summary>Mode configuration boundary for generation-specific flags, separate from any submission/checker chain.</summary>
public static class RuntimeInstanceFlagConfiguration
{
    public static PerTeamFlagTemplate Resolve(CompetitionModeConfiguration competition, CompetitionChallengeRules rules, ChallengeDefinition definition)
    {
        if (competition is AwdpCompetitionModeConfiguration awdp && rules is AwdpCompetitionChallengeRules awdpRules && definition is AwdpChallengeDefinition awdpDefinition)
        {
            var configuration = AwdpConfigurationResolver.Resolve(awdp, awdpRules, awdpDefinition);
            if (configuration.Runtime is not { Allocation: RuntimeAllocation.PerTeam, FlagSource: RuntimeFlagSource.PerTeam,
                Definition: ContainerRuntimeDefinition { Services: [{ FlagEnvironmentVariableName.Length: > 0 }] } })
                throw new InvalidOperationException("AWDP generation flags require a PerTeam Container runtime flag environment variable.");
            return configuration.FlagTemplate;
        }
        if (competition is LiveSoloCompetitionModeConfiguration && rules is LiveSoloCompetitionChallengeRules && definition is LiveSoloChallengeDefinition)
        {
            var runtime = TypedGameModeConfiguration.Runtime(definition.Runtime);
            if (runtime is not { Allocation: RuntimeAllocation.PerTeam, FlagSource: RuntimeFlagSource.PerTeam,
                Definition: ContainerRuntimeDefinition } || !((ContainerRuntimeDefinition)runtime.Definition).Services.Any(x => x.FlagEnvironmentVariableName is { Length: > 0 }))
                throw new InvalidOperationException("Generation-specific flags require a per-team environment injection target.");
            var template = rules.HasFlagTemplate ? rules.FlagTemplate : definition.HasFlagTemplate ? definition.FlagTemplate : competition.FlagTemplate;
            return new(template.Header, template.BodyTemplate, template.LeetLiteralText);
        }
        throw new InvalidOperationException("The mode does not support generation-specific Flag configuration.");
    }
}
