using NoCTF.Application.SystemProducers;

namespace NoCTF.GameModes.Awd.Configuration;

public sealed class AwdFlagInjectionConfigurationCatalog : IAwdFlagInjectionConfigurationCatalog
{
    public AwdFlagInjectionSettings? Get(string challengeConfigurationJson)
    {
        var injection = AwdConfigurationUpgrader.ParseChallenge(challengeConfigurationJson).FlagInjection;
        return injection is null ? null : new(injection.Command, injection.TimeoutSeconds);
    }
}
