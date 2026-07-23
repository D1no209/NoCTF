namespace NoCTF.GameModes.Awd.Configuration;

public sealed record AwdRoundSettings(int RoundDurationSeconds, int TotalRounds, int FlagValidityRounds);

public sealed class AwdRoundConfigurationCatalog
{
    public AwdRoundSettings Get(string competitionConfigurationJson)
    {
        var configuration = AwdConfigurationUpgrader.ParseCompetition(competitionConfigurationJson);
        return new(configuration.RoundDurationSeconds, configuration.TotalRounds, configuration.FlagValidityRounds);
    }
}
