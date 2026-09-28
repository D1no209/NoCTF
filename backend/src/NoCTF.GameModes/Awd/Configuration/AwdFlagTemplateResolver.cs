using NoCTF.GameModes.Flags;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;

namespace NoCTF.GameModes.Awd.Configuration;

public static class AwdFlagTemplateResolver
{
    public static PerTeamFlagTemplate Resolve(
        AwdCompetitionModeConfiguration competition,
        AwdCompetitionChallengeRules challenge)
    {
        return (challenge.HasFlagTemplate ? TypedGameModeConfiguration.Awd(challenge).FlagTemplate : null)
            ?? TypedGameModeConfiguration.Awd(competition).FlagTemplate
            ?? PerTeamFlagTemplate.Default;
    }
}
