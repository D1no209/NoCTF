using NoCTF.Domain.Challenges;

namespace NoCTF.GameModes.Awd.Configuration;

public sealed record AwdFlagInjectionSettings(string Command, int TimeoutSeconds, string? ServiceName);

public interface IAwdFlagInjectionConfigurationCatalog
{
    AwdFlagInjectionSettings? Get(AwdChallengeDefinition definition);
}

public sealed class AwdFlagInjectionConfigurationCatalog : IAwdFlagInjectionConfigurationCatalog
{
    public AwdFlagInjectionSettings? Get(AwdChallengeDefinition definition) =>
        definition.FlagInjectionCommand is null
            ? null
            : new(
                definition.FlagInjectionCommand,
                definition.FlagInjectionTimeoutSeconds ?? 30,
                definition.FlagInjectionServiceName);
}
