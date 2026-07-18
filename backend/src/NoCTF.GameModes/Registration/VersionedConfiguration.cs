using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NoCTF.GameModes.Registration;

internal static class VersionedConfiguration
{
    internal static readonly JsonSerializerOptions StrictOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static T Parse<T>(
        string json,
        int currentVersion,
        Func<JsonObject, int, JsonObject> upgrade)
    {
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new GameModeConfigurationException("Configuration must be a JSON object.");
        var version = root["schemaVersion"]?.GetValue<int>()
            ?? throw new GameModeConfigurationException("schemaVersion is required.");
        if (version > currentVersion)
            throw new GameModeConfigurationException($"schemaVersion {version} is newer than supported version {currentVersion}.");

        while (version < currentVersion)
        {
            root = upgrade(root, version);
            version = root["schemaVersion"]?.GetValue<int>()
                ?? throw new GameModeConfigurationException("An upgrader did not set schemaVersion.");
        }

        try
        {
            return root.Deserialize<T>(StrictOptions)
                ?? throw new GameModeConfigurationException("Configuration cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new GameModeConfigurationException(exception.Message, exception);
        }
    }
}

public sealed class GameModeConfigurationException : Exception
{
    public GameModeConfigurationException(string message) : base(message) { }
    public GameModeConfigurationException(string message, Exception innerException) : base(message, innerException) { }
}
