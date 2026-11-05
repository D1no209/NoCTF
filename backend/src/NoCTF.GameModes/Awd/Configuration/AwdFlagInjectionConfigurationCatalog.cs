using NoCTF.Domain.Challenges;

namespace NoCTF.GameModes.Awd.Configuration;

public sealed record AwdFlagInjectionSettings(string Command, int TimeoutSeconds, string? ServiceName);

public interface IAwdFlagInjectionConfigurationCatalog
{
    AwdFlagInjectionSettings? Get(ChallengeDefinition definition);
}

public sealed class AwdFlagInjectionConfigurationCatalog : IAwdFlagInjectionConfigurationCatalog
{
    public AwdFlagInjectionSettings? Get(ChallengeDefinition definition) =>
        definition.Break?.InjectionCommand is null
            ? null
            : new(
                definition.Break.InjectionCommand,
                definition.Break.InjectionTimeoutSeconds,
                definition.Break.InjectionServiceName);
}
