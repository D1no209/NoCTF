namespace NoCTF.GameModes.Awd.Configuration;

public sealed record AwdFlagInjectionSettings(string Command, int TimeoutSeconds, string? ServiceName);

public interface IAwdFlagInjectionConfigurationCatalog
{
    AwdFlagInjectionSettings? Get(string challengeConfigurationJson);
}

public sealed class AwdFlagInjectionConfigurationCatalog : IAwdFlagInjectionConfigurationCatalog
{
    public AwdFlagInjectionSettings? Get(string challengeConfigurationJson)
    {
        var injection = AwdConfigurationUpgrader.ParseChallenge(challengeConfigurationJson).FlagInjection;
        return injection is null
            ? null
            : new(injection.Command, injection.TimeoutSeconds, injection.ServiceName);
    }
}
