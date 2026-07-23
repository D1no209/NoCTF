namespace NoCTF.GameModes.Koh.Configuration;

public sealed record KohProducerSettings(
    int PollIntervalSeconds,
    Uri AgentUrl,
    IReadOnlyDictionary<string, Guid> TeamIdentifiers);

public sealed class KohProducerConfigurationCatalog
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
