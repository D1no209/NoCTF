using NoCTF.GameModes.Flags;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;

namespace NoCTF.GameModes.Ctf.Configuration;

public static class CtfFlagTemplateResolver
{
    public static PerTeamFlagTemplate Resolve(
        CtfCompetitionModeConfiguration competition,
        CtfCompetitionChallengeRules challenge)
    {
        return (challenge.HasFlagTemplate ? TypedGameModeConfiguration.Ctf(challenge).FlagTemplate : null)
            ?? TypedGameModeConfiguration.Ctf(competition).FlagTemplate
            ?? PerTeamFlagTemplate.Default;
    }
}
