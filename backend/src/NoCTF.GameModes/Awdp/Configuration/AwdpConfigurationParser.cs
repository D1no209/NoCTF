using NoCTF.GameModes.Registration;

namespace NoCTF.GameModes.Awdp.Configuration;

public static class AwdpConfigurationParser
{
    public static AwdpConfiguration ParseCompetition(string json) =>
        VersionedConfiguration.ParseCurrent<AwdpConfiguration>(
            json, AwdpConfiguration.CurrentSchemaVersion);

    public static AwdpChallengeConfiguration ParseChallenge(string json) =>
        VersionedConfiguration.ParseCurrent<AwdpChallengeConfiguration>(
            json, AwdpChallengeConfiguration.CurrentSchemaVersion);
}
