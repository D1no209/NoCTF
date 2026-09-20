using System.Text;
using System.Text.Json;

namespace NoCTF.Bot.Milky;

internal static class MilkyEventParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static MilkyGroupMessage? ParseGroupMessage(string json)
    {
        var envelope = JsonSerializer.Deserialize<MilkyEventEnvelope>(json, JsonOptions);
        if (envelope is null
            || !string.Equals(envelope.EventType, "message_receive", StringComparison.Ordinal)
            || envelope.Data.ValueKind != JsonValueKind.Object
            || !envelope.Data.TryGetProperty("message_scene", out var scene)
            || scene.GetString() != "group"
            || !TryGetInt64(envelope.Data, "peer_id", out var groupId)
            || !TryGetInt64(envelope.Data, "sender_id", out var senderId)
            || !TryGetInt64(envelope.Data, "message_seq", out var messageSequence)
            || !envelope.Data.TryGetProperty("segments", out var segments)
            || segments.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var text = new StringBuilder();
        foreach (var segment in segments.EnumerateArray())
        {
            if (segment.ValueKind != JsonValueKind.Object
                || !segment.TryGetProperty("type", out var type)
                || type.GetString() != "text"
                || !segment.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("text", out var value)
                || value.ValueKind != JsonValueKind.String)
            {
                continue;
            }
            text.Append(value.GetString());
        }
        var commandText = text.ToString().Trim();
        return commandText.Length == 0
            ? null
            : new(envelope.SelfId, groupId, senderId, messageSequence, commandText);
    }

    private static bool TryGetInt64(JsonElement value, string name, out long parsed)
    {
        parsed = 0;
        return value.TryGetProperty(name, out var property)
            && (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out parsed)
                || property.ValueKind == JsonValueKind.String
                && long.TryParse(property.GetString(), out parsed));
    }
}
