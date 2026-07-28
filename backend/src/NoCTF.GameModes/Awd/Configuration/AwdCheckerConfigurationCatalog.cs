using NoCTF.Application.Runtime.Configuration;

namespace NoCTF.GameModes.Awd.Configuration;

public sealed record AwdCheckerSettings(
    int CheckerIntervalSeconds,
    RunnerJobConfiguration? Checker);

public sealed class AwdCheckerConfigurationCatalog
{
    public AwdCheckerSettings Get(string competitionConfigurationJson, string challengeConfigurationJson)
    {
        var competition = AwdConfigurationUpgrader.ParseCompetition(competitionConfigurationJson);
        var challenge = AwdConfigurationUpgrader.ParseChallenge(challengeConfigurationJson);
        return new(
            challenge.CheckerIntervalSeconds ?? competition.CheckerIntervalSeconds,
            challenge.Checker?.Job);
    }
}
