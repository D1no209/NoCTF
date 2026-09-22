using NoCTF.Application.Runtime.Configuration;

namespace NoCTF.GameModes.Awd.Configuration;

public sealed record AwdCheckerSettings(
    int CheckerIntervalSeconds,
    RunnerJobConfiguration? Checker,
    bool CheckerAllowRoot = false);

public sealed class AwdCheckerConfigurationCatalog
{
    public AwdCheckerSettings Get(
        string competitionConfigurationJson,
        string challengeRulesJson,
        string challengeDefinitionJson)
    {
        var competition = AwdConfigurationParser.ParseCompetition(competitionConfigurationJson);
        var rules = AwdConfigurationParser.ParseChallenge(challengeRulesJson);
        var definition = AwdConfigurationParser.ParseChallenge(challengeDefinitionJson);
        return new(
            rules.CheckerIntervalSeconds ?? competition.CheckerIntervalSeconds,
            definition.Checker?.Job,
            definition.CheckerAllowRoot);
    }
}
