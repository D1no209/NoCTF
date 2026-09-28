using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Registration;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Infrastructure.Caching;

public sealed class FusionChallengeRuntimeTemplateCatalog(
    ChallengeRuntimeTemplateCatalog inner) : IChallengeRuntimeTemplateCatalog
{
    public ChallengeRuntimeTemplate? Get(ChallengeDefinition? definition) =>
        inner.Get(definition);
}

public sealed class FusionAwdRoundConfigurationCatalog(
    AwdRoundConfigurationCatalog inner) : IAwdRoundConfigurationCatalog
{
    public AwdRoundSettings Get(AwdCompetitionModeConfiguration configuration) =>
        inner.Get(configuration);
}

public sealed class FusionAwdFlagInjectionConfigurationCatalog(
    AwdFlagInjectionConfigurationCatalog inner) : IAwdFlagInjectionConfigurationCatalog
{
    public AwdFlagInjectionSettings? Get(AwdChallengeDefinition definition) =>
        inner.Get(definition);
}

public sealed class FusionKohProducerConfigurationCatalog(
    KohProducerConfigurationCatalog inner) : IKohProducerConfigurationCatalog
{
    public KohProducerSettings Get(
        KohCompetitionModeConfiguration competition,
        KohCompetitionChallengeRules challenge) => inner.Get(competition, challenge);
}
