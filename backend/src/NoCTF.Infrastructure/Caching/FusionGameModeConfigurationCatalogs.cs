using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Registration;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Infrastructure.Caching;

public sealed class FusionChallengeRuntimeTemplateCatalog(
    ChallengeRuntimeTemplateCatalog inner,
    IFusionCacheProvider caches) : IChallengeRuntimeTemplateCatalog
{
    private readonly IFusionCache cache = caches.GetCache(NoCtfCacheNames.LocalComputation);

    public ChallengeRuntimeTemplate? Get(GameMode mode, string challengeConfigurationJson) =>
        cache.GetOrSet<ChallengeRuntimeTemplate?>(
            $"runtime-template:{mode}:{ConfigurationCacheKey.Hash(challengeConfigurationJson)}",
            _ => inner.Get(mode, challengeConfigurationJson));
}

public sealed class FusionAwdRoundConfigurationCatalog(
    AwdRoundConfigurationCatalog inner,
    IFusionCacheProvider caches) : IAwdRoundConfigurationCatalog
{
    private readonly IFusionCache cache = caches.GetCache(NoCtfCacheNames.LocalComputation);

    public AwdRoundSettings Get(string competitionConfigurationJson) =>
        cache.GetOrSet(
            $"awd-round:{ConfigurationCacheKey.Hash(competitionConfigurationJson)}",
            _ => inner.Get(competitionConfigurationJson));
}

public sealed class FusionAwdFlagInjectionConfigurationCatalog(
    AwdFlagInjectionConfigurationCatalog inner,
    IFusionCacheProvider caches) : IAwdFlagInjectionConfigurationCatalog
{
    private readonly IFusionCache cache = caches.GetCache(NoCtfCacheNames.LocalComputation);

    public AwdFlagInjectionSettings? Get(string challengeConfigurationJson) =>
        cache.GetOrSet<AwdFlagInjectionSettings?>(
            $"awd-flag-injection:{ConfigurationCacheKey.Hash(challengeConfigurationJson)}",
            _ => inner.Get(challengeConfigurationJson));
}

public sealed class FusionKohProducerConfigurationCatalog(
    KohProducerConfigurationCatalog inner,
    IFusionCacheProvider caches) : IKohProducerConfigurationCatalog
{
    private readonly IFusionCache cache = caches.GetCache(NoCtfCacheNames.LocalComputation);

    public KohProducerSettings Get(
        string competitionConfigurationJson,
        string challengeRulesJson) =>
        cache.GetOrSet(
            $"koh-producer:{ConfigurationCacheKey.Hash(competitionConfigurationJson)}:{ConfigurationCacheKey.Hash(challengeRulesJson)}",
            _ => inner.Get(competitionConfigurationJson, challengeRulesJson));
}

internal static class ConfigurationCacheKey
{
    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
