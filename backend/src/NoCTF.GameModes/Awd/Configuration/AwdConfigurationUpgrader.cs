using System.Text.Json.Nodes;
using NoCTF.GameModes.Registration;

namespace NoCTF.GameModes.Awd.Configuration;

public static class AwdConfigurationUpgrader
{
    public static AwdConfiguration ParseCompetition(string json) =>
        VersionedConfiguration.Parse<AwdConfiguration>(json, AwdConfiguration.CurrentSchemaVersion, Upgrade);

    public static AwdChallengeConfiguration ParseChallenge(string json) =>
        VersionedConfiguration.Parse<AwdChallengeConfiguration>(json, AwdChallengeConfiguration.CurrentSchemaVersion, UpgradeChallenge);

    private static JsonObject Upgrade(JsonObject root, int fromVersion) =>
        throw new GameModeConfigurationException($"AWD schemaVersion {fromVersion} has no registered upgrader.");

    private static JsonObject UpgradeChallenge(JsonObject root, int fromVersion)
    {
        if (fromVersion != 1)
            throw new GameModeConfigurationException($"AWD challenge schemaVersion {fromVersion} has no registered upgrader.");
        root["checker"] = null;
        root["schemaVersion"] = 2;
        return root;
    }
}
