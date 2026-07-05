using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private static readonly Regex EnvironmentKeyPattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

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
            if (!string.IsNullOrWhiteSpace(node.OrchestrationJson) && node.OrchestrationJson.Trim() != "{}")
            {
                AppendBoth("    x-noctf-orchestration: |-");
                foreach (var line in node.OrchestrationJson.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                    AppendBoth($"      {line}");
            }

            var dependsOn = ReadStringArray(node.DependsOnJson).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
            if (dependsOn.Count > 0)
            {
                AppendBoth("    depends_on:");
                foreach (var item in dependsOn)
                    AppendBoth($"      - {Quote(item)}");
            }

            var environment = ReadStringDictionary(node.EnvironmentJson);
            foreach (var key in environment.Keys)
                ValidateEnvironmentKey(key);
            var redactedEnvironment = environment.Keys.ToDictionary(key => key, _ => "[REDACTED]", StringComparer.Ordinal);
            foreach (var flag in flags.Where(f => f.IsDynamic && f.NodeId == node.Id))
            {
                if (flag.InjectionType == PenetrationFlagInjectionType.File)
                    throw new InvalidOperationException("file_injection_not_supported");
                if (string.IsNullOrWhiteSpace(flag.InjectionKey))
                    throw new InvalidOperationException("dynamic_flag_injection_key_required");
                ValidateEnvironmentKey(flag.InjectionKey);
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
            AppendBoth("    user: \"1000:1000\"");
            AppendBoth("    read_only: true");
            var limits = ReadResourceLimits(node.ResourceLimitJson);
            AppendBoth($"    pids_limit: {limits.PidsLimit}");
            AppendBoth("    deploy:");
            AppendBoth("      resources:");
            AppendBoth("        limits:");
            AppendBoth($"          cpus: {Quote(limits.Cpus)}");
            AppendBoth($"          memory: {Quote(limits.Memory)}");
            AppendBoth("    restart: unless-stopped");
        }

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

    private static PenetrationNodeResourceLimits ReadResourceLimits(string json)
    {
        var limits = new PenetrationNodeResourceLimits("0.50", "256M", 128);
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return limits;

            var cpus = ReadString(document.RootElement, "cpus")
                       ?? ReadString(document.RootElement, "cpu")
                       ?? limits.Cpus;
            var memory = ReadString(document.RootElement, "memory")
                         ?? ReadString(document.RootElement, "memoryLimit")
                         ?? limits.Memory;
            var pids = ReadInt(document.RootElement, "pidsLimit")
                       ?? ReadInt(document.RootElement, "pids")
                       ?? limits.PidsLimit;

            if (string.IsNullOrWhiteSpace(cpus))
                cpus = limits.Cpus;
            if (string.IsNullOrWhiteSpace(memory))
                memory = limits.Memory;

            return new PenetrationNodeResourceLimits(cpus.Trim(), memory.Trim(), Math.Clamp(pids, 1, 512));
        }
        catch
        {
            return limits;
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ValueKind == JsonValueKind.Number
                ? property.GetRawText()
                : null;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
            return number;
        return property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out number)
            ? number
            : null;
    }

    private static void ValidateEnvironmentKey(string key)
    {
        if (!EnvironmentKeyPattern.IsMatch(key.Trim()))
            throw new InvalidOperationException("invalid_environment_key");
    }

    private sealed record PenetrationNodeResourceLimits(string Cpus, string Memory, int PidsLimit);
}
