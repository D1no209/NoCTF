using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Registration;

internal static class TypedConfigurationTestExtensions
{
    public static CompetitionChallengeRules CreateDefaultRulesForTest(
        this GameModeChallengeConfigurationCatalog catalog,
        GameMode mode) => catalog.CreateDefaultRules(mode, Guid.NewGuid());

    public static ChallengeDefinition CreateDefaultDefinitionForTest(
        this GameModeChallengeConfigurationCatalog catalog,
        GameMode mode) => catalog.CreateDefaultDefinition(mode, Guid.NewGuid());
}
