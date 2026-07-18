using System.Text.Json.Nodes;
using NoCTF.GameModes.Registration;

namespace NoCTF.GameModes.Ctf.Configuration;

public static class CtfConfigurationUpgrader
{
    public static CtfConfiguration ParseCompetition(string json) =>
        VersionedConfiguration.Parse<CtfConfiguration>(json, CtfConfiguration.CurrentSchemaVersion, Upgrade);

    public static CtfChallengeConfiguration ParseChallenge(string json) =>
        VersionedConfiguration.Parse<CtfChallengeConfiguration>(json, CtfChallengeConfiguration.CurrentSchemaVersion, Upgrade);

    private static JsonObject Upgrade(JsonObject root, int fromVersion) =>
        throw new GameModeConfigurationException($"CTF schemaVersion {fromVersion} has no registered upgrader.");
}
