using System.Text;
using System.Text.Json;
using NoCTF.Core;

namespace NoCTF.Plugins.Penetration;

public sealed record PenetrationComposeBuildResult(
    string ComposeYaml,
    string RedactedComposeYaml,
    PenetrationNode EntryNode,
    int EntryContainerPort);

public class PenetrationComposeBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PenetrationComposeBuildResult Build(
        Challenge challenge,
        TeamChallengeInstance instance,
        IReadOnlyList<PenetrationNode> nodes,
        IReadOnlyList<PenetrationFlag> flags,
        IReadOnlyList<DynamicFlagInstance> dynamicFlags)
    {
        var entry = nodes
            .OrderBy(n => n.DisplayOrder)
            .FirstOrDefault(n => n.IsEntry)
            ?? throw new InvalidOperationException("penetration_entry_node_required");

        var dynamicByFlagId = dynamicFlags.ToDictionary(f => f.FlagId);
        var builder = new StringBuilder();
        var redactedBuilder = new StringBuilder();
        void AppendBoth(string line)
        {
            builder.AppendLine(line);
            redactedBuilder.AppendLine(line);
        }

        AppendBoth("services:");

        foreach (var node in nodes.OrderBy(n => n.DisplayOrder).ThenBy(n => n.Name))
        {
            var serviceName = node.Name.Trim();
            AppendBoth($"  {serviceName}:");
            AppendBoth($"    image: {Quote(node.Image)}");
            if (!string.IsNullOrWhiteSpace(node.Command))
                AppendBoth($"    command: {Quote(node.Command)}");

            var dependsOn = ReadStringArray(node.DependsOnJson).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
            if (dependsOn.Count > 0)
            {
                AppendBoth("    depends_on:");
                foreach (var item in dependsOn)
                    AppendBoth($"      - {Quote(item)}");
            }

            var environment = ReadStringDictionary(node.EnvironmentJson);
            var redactedEnvironment = environment.Keys.ToDictionary(key => key, _ => "[REDACTED]", StringComparer.Ordinal);
            foreach (var flag in flags.Where(f => f.IsDynamic && f.NodeId == node.Id))
            {
                if (flag.InjectionType == PenetrationFlagInjectionType.File)
                    throw new InvalidOperationException("file_injection_not_supported");
                if (string.IsNullOrWhiteSpace(flag.InjectionKey))
                    throw new InvalidOperationException("dynamic_flag_injection_key_required");
                if (dynamicByFlagId.TryGetValue(flag.Id, out var dynamicFlag))
                {
                    environment[flag.InjectionKey] = PenetrationFlagService.FormatFlag(challenge, ResolveDynamicFlagValue(dynamicFlag));
                    redactedEnvironment[flag.InjectionKey] = "[REDACTED]";
                }
            }

            if (environment.Count > 0)
            {
                builder.AppendLine("    environment:");
                foreach (var (key, value) in environment.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
                    builder.AppendLine($"      {key}: {Quote(value)}");

                redactedBuilder.AppendLine("    environment:");
                foreach (var (key, value) in redactedEnvironment.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
                    redactedBuilder.AppendLine($"      {key}: {Quote(value)}");
            }

            var entryPort = GetPrimaryContainerPort(node, challenge.ExposedPort);
            if (node.IsEntry && entryPort > 0)
            {
                AppendBoth("    ports:");
                AppendBoth($"      - \"{entryPort}\"");
            }

            AppendBoth("    labels:");
            foreach (var (key, value) in BuildLabels(challenge, instance, node))
                AppendBoth($"      {key}: {Quote(value)}");

            AppendBoth("    security_opt:");
            AppendBoth("      - no-new-privileges:true");
            AppendBoth("    cap_drop:");
            AppendBoth("      - ALL");
            AppendBoth("    restart: unless-stopped");
        }

        AppendBoth("networks:");
        AppendBoth("  default:");
        AppendBoth($"    name: {Quote($"{instance.ComposeProjectName}_default")}");

        return new PenetrationComposeBuildResult(
            ComposeYaml: builder.ToString(),
            RedactedComposeYaml: redactedBuilder.ToString(),
            EntryNode: entry,
            EntryContainerPort: GetPrimaryContainerPort(entry, challenge.ExposedPort));
    }

    public static int GetPrimaryContainerPort(PenetrationNode node, int? fallback)
    {
        var ports = ReadPortNumbers(node.PortsJson);
        if (ports.Count > 0) return ports[0];
        return fallback is > 0 ? fallback.Value : node.IsEntry ? 80 : 0;
    }

    private static Dictionary<string, string> BuildLabels(
        Challenge challenge,
        TeamChallengeInstance instance,
        PenetrationNode node)
        => new()
        {
            ["noctf.kind"] = "penetration",
            ["competitionId"] = challenge.CompetitionId.ToString(),
            ["challengeId"] = challenge.Id.ToString(),
            ["teamId"] = instance.TeamId.ToString(),
            ["instanceId"] = instance.Id.ToString(),
            ["nodeId"] = node.Id.ToString(),
        };

    private static string ResolveDynamicFlagValue(DynamicFlagInstance dynamicFlag)
        => !string.IsNullOrWhiteSpace(dynamicFlag.PlainValue)
            ? dynamicFlag.PlainValue
            : dynamicFlag.ValueSecret;

    private static List<int> ReadPortNumbers(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
            if (doc.RootElement.ValueKind == JsonValueKind.Number && doc.RootElement.TryGetInt32(out var single))
                return [single];
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

            var result = new List<int>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var number))
                    result.Add(number);
                else if (item.ValueKind == JsonValueKind.Object &&
                         item.TryGetProperty("containerPort", out var port) &&
                         port.TryGetInt32(out var containerPort))
                    result.Add(containerPort);
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    private static Dictionary<string, string> ReadStringDictionary(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static List<string> ReadStringArray(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string Quote(string value)
        => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
