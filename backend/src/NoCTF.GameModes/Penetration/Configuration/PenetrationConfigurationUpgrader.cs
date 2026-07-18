using System.Text.Json.Nodes;
using NoCTF.GameModes.Registration;

namespace NoCTF.GameModes.Penetration.Configuration;

public static class PenetrationConfigurationUpgrader
{
    public static PenetrationConfiguration ParseCompetition(string json) =>
        VersionedConfiguration.Parse<PenetrationConfiguration>(json, PenetrationConfiguration.CurrentSchemaVersion, Upgrade);

    public static PenetrationChallengeConfiguration ParseChallenge(string json) =>
        VersionedConfiguration.Parse<PenetrationChallengeConfiguration>(json, PenetrationChallengeConfiguration.CurrentSchemaVersion, Upgrade);

    private static JsonObject Upgrade(JsonObject root, int fromVersion) =>
        throw new GameModeConfigurationException($"Penetration schemaVersion {fromVersion} has no registered upgrader.");
}
