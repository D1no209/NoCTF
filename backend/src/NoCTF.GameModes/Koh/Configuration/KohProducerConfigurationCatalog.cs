namespace NoCTF.GameModes.Koh.Configuration;

public sealed record KohProducerSettings(
    int PollIntervalSeconds,
    long ControlPointsPerInterval);

public sealed class KohProducerConfigurationCatalog
{
    public KohProducerSettings Get(string competitionConfigurationJson, string challengeConfigurationJson)
    {
        var competition = KohConfigurationUpgrader.ParseCompetition(competitionConfigurationJson);
        var challenge = KohConfigurationUpgrader.ParseChallenge(challengeConfigurationJson);
        return new(
            challenge.PollIntervalSeconds ?? competition.PollIntervalSeconds,
            challenge.ControlPointsPerInterval ?? competition.ControlPointsPerInterval);
    }
}
