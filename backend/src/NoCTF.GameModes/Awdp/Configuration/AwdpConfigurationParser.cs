using NoCTF.GameModes.Registration;

namespace NoCTF.GameModes.Awdp.Configuration;

public static class AwdpConfigurationParser
{
    public static AwdpConfiguration ParseCompetition(string json) =>
        CurrentConfigurationParser.Parse<AwdpConfiguration>(
            json,
            AwdpConfiguration.CurrentSchemaVersion);

    public static AwdpChallengeConfiguration ParseChallenge(string json) =>
        CurrentConfigurationParser.Parse<AwdpChallengeConfiguration>(
            json,
            AwdpChallengeConfiguration.CurrentSchemaVersion);
}
