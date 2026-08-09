using NoCTF.GameModes.Flags;

namespace NoCTF.GameModes.Ctf.Configuration;

public static class CtfFlagTemplateResolver
{
    public static PerTeamFlagTemplate Resolve(
        string competitionConfigurationJson,
        string challengeDefinitionJson)
    {
        var competition = CtfConfigurationUpgrader.ParseCompetition(
            competitionConfigurationJson);
        var challenge = CtfConfigurationUpgrader.ParseChallenge(
            challengeDefinitionJson);
        return challenge.FlagTemplate
            ?? competition.FlagTemplate
            ?? PerTeamFlagTemplate.Default;
    }
}
