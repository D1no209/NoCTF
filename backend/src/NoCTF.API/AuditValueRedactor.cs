using System.Text.Json;
using System.Text.Json.Nodes;

namespace NoCTF.API;

public static class AuditValueRedactor
{
    private static readonly string[] SensitiveKeyParts =
    [
        "password",
        "token",
        "secret",
        "flag",
        "jwt",
        "composeyaml",
        "orchestrationjson",
        "penetrationconfigjson",
        "checkerconfig",
        "environmentvariables",
        "env"
    ];

    public static string? Serialize(object? value, JsonSerializerOptions options)
    {
        if (value is null)
            return null;

        return RedactJson(JsonSerializer.Serialize(value, value.GetType(), options));
    }

    public static string RedactJson(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is null)
                return json;

            RedactNode(node, parentKey: null);
            return node.ToJsonString();
        }
        catch
        {
            return json;
        }
    }

    private static void RedactNode(JsonNode node, string? parentKey)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(kv => kv.Key).ToList())
            {
                if (IsSensitiveKey(key))
                {
                    obj[key] = JsonValue.Create("[REDACTED]");
                    continue;
                }

                if (obj[key] is { } child)
                    RedactNode(child, key);
            }
            return;
        }

        if (node is JsonArray arr)
        {
            foreach (var child in arr)
            {
                if (child is not null)
                    RedactNode(child, parentKey);
            }
        }
    }

    private static bool IsSensitiveKey(string key)
    {
        var normalized = key.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return SensitiveKeyParts.Any(normalized.Contains);
    }
}
