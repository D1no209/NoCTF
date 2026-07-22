using NoCTF.Application.SystemProducers;

namespace NoCTF.GameModes.Awd.Configuration;

public sealed class AwdRoundConfigurationCatalog : IAwdRoundConfigurationCatalog
{
    public AwdRoundSettings Get(string competitionConfigurationJson)
    {
        var configuration = AwdConfigurationUpgrader.ParseCompetition(competitionConfigurationJson);
        return new(configuration.RoundDurationSeconds, configuration.TotalRounds, configuration.FlagValidityRounds);
    }
}
