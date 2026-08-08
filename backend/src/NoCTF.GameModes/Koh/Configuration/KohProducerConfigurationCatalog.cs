namespace NoCTF.GameModes.Koh.Configuration;

public sealed record KohProducerSettings(
    int PollIntervalSeconds,
    long ControlPointsPerInterval);

public interface IKohProducerConfigurationCatalog
{
    KohProducerSettings Get(string competitionConfigurationJson, string challengeRulesJson);
}

public sealed class KohProducerConfigurationCatalog : IKohProducerConfigurationCatalog
{
    public KohProducerSettings Get(string competitionConfigurationJson, string challengeRulesJson)
    {
        var competition = KohConfigurationUpgrader.ParseCompetition(competitionConfigurationJson);
        var challenge = KohConfigurationUpgrader.ParseChallenge(challengeRulesJson);
        return new(
            challenge.PollIntervalSeconds ?? competition.PollIntervalSeconds,
            challenge.ControlPointsPerInterval ?? competition.ControlPointsPerInterval);
    }
}
