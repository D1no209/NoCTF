using NoCTF.GameModes.Flags;

namespace NoCTF.GameModes.Ctf.Configuration;

public static class CtfFlagTemplateResolver
{
    public static PerTeamFlagTemplate Resolve(
        string competitionConfigurationJson,
        string challengeRulesJson)
    {
        var competition = CtfConfigurationParser.ParseCompetition(
            competitionConfigurationJson);
        var challenge = CtfConfigurationParser.ParseRules(
            challengeRulesJson);
        return challenge.FlagTemplate
            ?? competition.FlagTemplate
            ?? PerTeamFlagTemplate.Default;
    }
}
