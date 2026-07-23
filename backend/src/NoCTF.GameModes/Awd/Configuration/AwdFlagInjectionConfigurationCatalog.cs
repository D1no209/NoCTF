namespace NoCTF.GameModes.Awd.Configuration;

public sealed record AwdFlagInjectionSettings(IReadOnlyList<string> Command, int TimeoutSeconds);

public sealed class AwdFlagInjectionConfigurationCatalog
{
    public AwdFlagInjectionSettings? Get(string challengeConfigurationJson)
    {
        var injection = AwdConfigurationUpgrader.ParseChallenge(challengeConfigurationJson).FlagInjection;
        return injection is null ? null : new(injection.Command, injection.TimeoutSeconds);
    }
}
