namespace NoCTF.GameModes.Awd.Configuration;

public sealed record AwdRoundSettings(int HardeningDurationSeconds, int RoundDurationSeconds);

public sealed class AwdRoundConfigurationCatalog
{
    public AwdRoundSettings Get(string competitionConfigurationJson)
    {
        var configuration = AwdConfigurationUpgrader.ParseCompetition(competitionConfigurationJson);
        return new(configuration.HardeningDurationSeconds, configuration.RoundDurationSeconds);
    }
}
