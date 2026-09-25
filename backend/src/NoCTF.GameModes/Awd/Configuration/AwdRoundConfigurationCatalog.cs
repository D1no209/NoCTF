using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Awd.Configuration;

public sealed record AwdRoundSettings(int HardeningDurationSeconds, int RoundDurationSeconds);

public interface IAwdRoundConfigurationCatalog
{
    AwdRoundSettings Get(AwdCompetitionModeConfiguration configuration);
}

public sealed class AwdRoundConfigurationCatalog : IAwdRoundConfigurationCatalog
{
    public AwdRoundSettings Get(AwdCompetitionModeConfiguration configuration) =>
        new(configuration.HardeningDurationSeconds, configuration.RoundDurationSeconds);
}
