using NoCTF.GameModes.Flags;

namespace NoCTF.GameModes.Awd.Configuration;

public static class AwdFlagTemplateResolver
{
    public static PerTeamFlagTemplate Resolve(
        string competitionConfigurationJson,
        string challengeRulesJson)
    {
        var competition = AwdConfigurationUpgrader.ParseCompetition(
            competitionConfigurationJson);
        var challenge = AwdConfigurationUpgrader.ParseChallenge(
            challengeRulesJson);
        return challenge.FlagTemplate
            ?? competition.FlagTemplate
            ?? PerTeamFlagTemplate.Default;
    }
}
