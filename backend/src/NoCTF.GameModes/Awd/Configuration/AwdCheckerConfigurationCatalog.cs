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
        AwdChallengeDefinition definition)
    {
        var checker = definition.Checker is null
            ? null
            : new RunnerJobConfiguration(
                definition.Checker.Image,
                definition.StringItems
                    .Where(item => item.Kind == ChallengeDefinitionStringKind.CheckerCommand)
                    .OrderBy(item => item.Position)
                    .Select(item => item.Value)
                    .ToArray(),
                definition.StringItems
                    .Where(item => item.Kind == ChallengeDefinitionStringKind.CheckerEnvironment)
                    .ToDictionary(item => item.Key!, item => item.Value, StringComparer.Ordinal),
                definition.Checker.TimeoutSeconds);
        return new(
            rules.CheckerIntervalSeconds ?? competition.CheckerIntervalSeconds,
            checker,
            definition.Checker?.TargetServiceName);
    }
}
