using NoCTF.Application.SystemProducers;

namespace NoCTF.GameModes.Awd.Configuration;

public sealed class AwdCheckerConfigurationCatalog : IAwdCheckerConfigurationCatalog
{
    public AwdCheckerSettings Get(string competitionConfigurationJson, string challengeConfigurationJson)
    {
        var competition = AwdConfigurationUpgrader.ParseCompetition(competitionConfigurationJson);
        return new(competition.RoundDurationSeconds, AwdConfigurationUpgrader.ParseChallenge(challengeConfigurationJson).Checker);
    }
}
