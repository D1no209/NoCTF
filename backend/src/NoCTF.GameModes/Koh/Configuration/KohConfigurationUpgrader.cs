using System.Text.Json.Nodes;
using NoCTF.GameModes.Registration;

namespace NoCTF.GameModes.Koh.Configuration;

public static class KohConfigurationUpgrader
{
    public static KohConfiguration ParseCompetition(string json) =>
        VersionedConfiguration.Parse<KohConfiguration>(json, KohConfiguration.CurrentSchemaVersion, Upgrade);

    public static KohChallengeConfiguration ParseChallenge(string json) =>
        VersionedConfiguration.Parse<KohChallengeConfiguration>(json, KohChallengeConfiguration.CurrentSchemaVersion, Upgrade);

    private static JsonObject Upgrade(JsonObject root, int fromVersion) =>
        throw new GameModeConfigurationException($"KoH schemaVersion {fromVersion} has no registered upgrader.");
}
