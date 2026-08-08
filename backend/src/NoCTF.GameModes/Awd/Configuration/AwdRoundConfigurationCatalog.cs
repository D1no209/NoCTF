namespace NoCTF.GameModes.Awd.Configuration;

public sealed record AwdRoundSettings(int HardeningDurationSeconds, int RoundDurationSeconds);

public interface IAwdRoundConfigurationCatalog
{
    AwdRoundSettings Get(string competitionConfigurationJson);
}

public sealed class AwdRoundConfigurationCatalog : IAwdRoundConfigurationCatalog
{
    public AwdRoundSettings Get(string competitionConfigurationJson)
    {
        var configuration = AwdConfigurationUpgrader.ParseCompetition(competitionConfigurationJson);
        return new(configuration.HardeningDurationSeconds, configuration.RoundDurationSeconds);
    }
}
