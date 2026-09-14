using System.Text.Json.Nodes;
using NoCTF.GameModes.Registration;
using NoCTF.Domain.Challenges;

namespace NoCTF.GameModes.Ctf.Configuration;

public static class CtfConfigurationUpgrader
{
    public static CtfConfiguration ParseCompetition(string json) =>
        VersionedConfiguration.Parse<CtfConfiguration>(json, CtfConfiguration.CurrentSchemaVersion, UpgradeCompetition);

    public static CtfChallengeConfiguration ParseChallenge(string json) =>
        VersionedConfiguration.Parse<CtfChallengeConfiguration>(json, CtfChallengeConfiguration.CurrentSchemaVersion, UpgradeChallenge);

    private static JsonObject UpgradeCompetition(JsonObject root, int fromVersion) =>
        throw new GameModeConfigurationException($"CTF schemaVersion {fromVersion} has no registered upgrader.");

    private static JsonObject UpgradeChallenge(JsonObject root, int fromVersion)
    {
        if (fromVersion != 2)
            throw new GameModeConfigurationException($"CTF schemaVersion {fromVersion} has no registered upgrader.");

        root["interactionKind"] = (int)CtfInteractionKind.FlagSubmission;
        root["schemaVersion"] = 3;
        return root;
    }
}
