using System.Text.Json.Nodes;
using NoCTF.GameModes.Registration;

namespace NoCTF.GameModes.Awdp.Configuration;

public static class AwdpConfigurationUpgrader
{
    public static AwdpConfiguration ParseCompetition(string json) =>
        VersionedConfiguration.Parse<AwdpConfiguration>(json, AwdpConfiguration.CurrentSchemaVersion, Upgrade);

    public static AwdpChallengeConfiguration ParseChallenge(string json) =>
        VersionedConfiguration.Parse<AwdpChallengeConfiguration>(json, AwdpChallengeConfiguration.CurrentSchemaVersion, Upgrade);

    private static JsonObject Upgrade(JsonObject root, int fromVersion) =>
        throw new GameModeConfigurationException($"AWDP schemaVersion {fromVersion} has no registered upgrader.");
}
