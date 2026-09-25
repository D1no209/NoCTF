using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.GameModes.Koh.Configuration;

public sealed record KohProducerSettings(
    int PollIntervalSeconds,
    long ControlPointsPerInterval);

public interface IKohProducerConfigurationCatalog
{
    KohProducerSettings Get(
        KohCompetitionModeConfiguration competition,
        KohCompetitionChallengeRules challenge);
}

public sealed class KohProducerConfigurationCatalog : IKohProducerConfigurationCatalog
{
    public KohProducerSettings Get(
        KohCompetitionModeConfiguration competition,
        KohCompetitionChallengeRules challenge) => new(
        challenge.PollIntervalSeconds ?? competition.PollIntervalSeconds,
        challenge.ControlPointsPerInterval ?? competition.ControlPointsPerInterval);
}
