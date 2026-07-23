using NoCTF.Application.Runtime.Ports;

namespace NoCTF.GameModes.Awd.Configuration;

public sealed record AwdCheckerSettings(int RoundDurationSeconds, RunnerJobConfiguration? Checker);

public sealed class AwdCheckerConfigurationCatalog
{
    public AwdCheckerSettings Get(string competitionConfigurationJson, string challengeConfigurationJson)
    {
        var competition = AwdConfigurationUpgrader.ParseCompetition(competitionConfigurationJson);
        return new(competition.RoundDurationSeconds, AwdConfigurationUpgrader.ParseChallenge(challengeConfigurationJson).Checker);
    }
}
