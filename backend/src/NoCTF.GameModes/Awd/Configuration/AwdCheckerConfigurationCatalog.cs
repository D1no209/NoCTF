using NoCTF.Application.Runtime.Configuration;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Awd.Configuration;

public sealed record AwdCheckerSettings(
    int CheckerIntervalSeconds,
    RunnerJobConfiguration? Checker,
    string? TargetServiceName = null);

public sealed class AwdCheckerConfigurationCatalog
{
    public AwdCheckerSettings Get(
        AwdCompetitionModeConfiguration competition,
        AwdCompetitionChallengeRules rules,
        ChallengeDefinition definition)
    {
        var checker = Registration.TypedGameModeConfiguration.Checker(definition);
        return new(
            rules.CheckerIntervalSeconds ?? competition.CheckerIntervalSeconds,
            checker,
            definition.Check?.TargetServiceName);
    }
}
