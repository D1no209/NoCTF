using NoCTF.Application.SystemProducers;

namespace NoCTF.GameModes.Koh.Configuration;

public sealed class KohProducerConfigurationCatalog : IKohProducerConfigurationCatalog
{
    public KohProducerSettings Get(string competitionConfigurationJson, string challengeConfigurationJson)
    {
        var competition = KohConfigurationUpgrader.ParseCompetition(competitionConfigurationJson);
        var challenge = KohConfigurationUpgrader.ParseChallenge(challengeConfigurationJson);
        return new(
            competition.PollIntervalSeconds,
            new Uri(challenge.AgentUrl, UriKind.Absolute),
            challenge.TeamIdentifiers ?? new Dictionary<string, Guid>(StringComparer.Ordinal));
    }
}
