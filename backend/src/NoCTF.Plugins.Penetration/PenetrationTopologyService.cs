using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Plugins.Penetration;

public class PenetrationTopologyService(ApplicationDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex ServiceNamePattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);
    private static readonly string[] ForbiddenTokens =
    [
        "privileged:",
        "network_mode: host",
        "pid: host",
        "ipc: host",
        "devices:",
        "cap_add:",
        "security_opt:",
        "extra_hosts:",
        "/var/run/docker.sock"
    ];

    public async Task<PenetrationTopologyDto> GetTemplateTopologyAsync(Guid templateId, CancellationToken ct)
    {
        var template = await db.ChallengeTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == templateId, ct);
        var topology = await db.PenetrationTopologyTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.ChallengeTemplateId == templateId, ct);
        if (topology is null) return new PenetrationTopologyDto { Code = "not_configured" };

        var nodes = await db.PenetrationNodeTemplates
            .AsNoTracking()
            .Where(n => n.TopologyTemplateId == topology.Id)
            .OrderBy(n => n.DisplayOrder)
            .ThenBy(n => n.Name)
            .ToListAsync(ct);
        var flags = await db.PenetrationFlagTemplates
            .AsNoTracking()
            .Where(f => f.TopologyTemplateId == topology.Id)
            .OrderBy(f => f.Stage)
            .ToListAsync(ct);

        var dto = ToDto(topology, nodes, flags);
        dto.Config = ReadConfigJson(template?.PenetrationConfigJson);
        return dto;
    }

    public async Task<PenetrationTopologyDto> SaveTemplateTopologyAsync(
        Guid templateId,
        PenetrationTopologyDocument document,
        CancellationToken ct)
    {
        var template = await db.ChallengeTemplates.FirstOrDefaultAsync(c => c.Id == templateId, ct)
            ?? throw new InvalidOperationException("challenge_template_not_found");

        EnsurePenetrationType(template.TypeId);
        Validate(document);

        var now = DateTime.UtcNow;
        var existing = await db.PenetrationTopologyTemplates.FirstOrDefaultAsync(t => t.ChallengeTemplateId == templateId, ct);
        var existingFlags = existing is null
            ? new Dictionary<Guid, PenetrationFlagTemplate>()
            : await db.PenetrationFlagTemplates
                .Where(f => f.TopologyTemplateId == existing.Id)
                .ToDictionaryAsync(f => f.Id, ct);

        if (existing is not null)
        {
            var oldNodes = await db.PenetrationNodeTemplates.Where(n => n.TopologyTemplateId == existing.Id).ToListAsync(ct);
            var oldFlags = await db.PenetrationFlagTemplates.Where(f => f.TopologyTemplateId == existing.Id).ToListAsync(ct);
            db.PenetrationNodeTemplates.RemoveRange(oldNodes);
            db.PenetrationFlagTemplates.RemoveRange(oldFlags);
        }

        var topology = existing ?? new PenetrationTopologyTemplate
        {
            Id = Guid.NewGuid(),
            ChallengeTemplateId = templateId,
            CreatedAt = now,
        };

        topology.Name = Clean(document.Name) ?? template.Title;
        topology.Description = Clean(document.Description);
        topology.NetworkConfigJson = JsonOrDefault(document.NetworkConfig, "{}");
        topology.EntryConfigJson = JsonOrDefault(document.EntryConfig, "{}");
        topology.HealthcheckConfigJson = JsonOrDefault(document.HealthcheckConfig, "{}");
        topology.UpdatedAt = now;
        if (existing is null) db.PenetrationTopologyTemplates.Add(topology);

        template.PenetrationConfigJson = JsonSerializer.Serialize(document.Config ?? new PenetrationRuntimeConfig(), JsonOptions);
        template.DeploymentType = ChallengeDeploymentType.DynamicContainer;
        template.ContainerMode = ChallengeContainerMode.DockerCompose;
        template.UpdatedAt = now;

        var nodeMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var nodes = document.Nodes.Select((node, index) =>
        {
            var id = Guid.NewGuid();
            nodeMap[node.Name.Trim()] = id;
            return new PenetrationNodeTemplate
            {
                Id = id,
                TopologyTemplateId = topology.Id,
                Name = node.Name.Trim(),
                Role = Clean(node.Role) ?? string.Empty,
                Image = node.Image.Trim(),
                Command = Clean(node.Command),
                EntrypointJson = JsonOrDefault(node.Entrypoint, "[]"),
                EnvironmentJson = JsonOrDefault(node.Environment, "{}"),
                PortsJson = JsonOrDefault(node.Ports, "[]"),
                VolumesJson = JsonOrDefault(node.Volumes, "[]"),
                NetworksJson = JsonOrDefault(node.Networks, "[]"),
                DependsOnJson = JsonOrDefault(node.DependsOn, "[]"),
                IsEntry = node.IsEntry,
                IsInternal = node.IsEntry ? node.IsInternal : true,
                ResourceLimitJson = JsonOrDefault(node.ResourceLimit, "{}"),
                HealthcheckJson = JsonOrDefault(node.Healthcheck, "{}"),
                DisplayOrder = node.DisplayOrder == 0 ? index + 1 : node.DisplayOrder,
                CreatedAt = now,
                UpdatedAt = now,
            };
        }).ToList();

        var flags = document.Flags.Select(flag =>
        {
            var nodeId = ResolveNodeId(flag.NodeId, flag.NodeName, nodeMap);
            existingFlags.TryGetValue(flag.Id ?? Guid.Empty, out var oldFlag);
            var secret = flag.IsDynamic ? null : Clean(flag.ValueSecret) ?? oldFlag?.ValueSecret;
            return new PenetrationFlagTemplate
            {
                Id = Guid.NewGuid(),
                TopologyTemplateId = topology.Id,
                NodeTemplateId = nodeId,
                Name = string.IsNullOrWhiteSpace(flag.Name) ? $"Stage {flag.Stage}" : flag.Name.Trim(),
                Stage = flag.Stage,
                ValueSecret = secret,
                ValueHash = secret is null ? oldFlag?.ValueHash : HashSecret(secret),
                Score = flag.Score,
                IsDynamic = flag.IsDynamic,
                Visible = flag.Visible,
                InjectionType = flag.InjectionType,
                InjectionKey = Clean(flag.InjectionKey),
                HintAfterSolved = Clean(flag.HintAfterSolved),
                CreatedAt = now,
                UpdatedAt = now,
            };
        }).ToList();

        db.PenetrationNodeTemplates.AddRange(nodes);
        db.PenetrationFlagTemplates.AddRange(flags);
        await db.SaveChangesAsync(ct);

        var dto = ToDto(topology, nodes, flags);
        dto.Config = ReadConfigJson(template.PenetrationConfigJson);
        return dto;
    }

    public async Task<PenetrationTopologyDto> CloneTemplateToChallengeAsync(
        Guid templateId,
        Guid challengeId,
        CancellationToken ct)
    {
        var challenge = await db.Challenges.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == challengeId, ct)
            ?? throw new InvalidOperationException("challenge_not_found");
        EnsurePenetrationType(challenge.TypeId);

        var template = await db.ChallengeTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new InvalidOperationException("challenge_template_not_found");
        var source = await db.PenetrationTopologyTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.ChallengeTemplateId == templateId, ct)
            ?? throw new InvalidOperationException("penetration_topology_not_configured");
        var sourceNodes = await db.PenetrationNodeTemplates
            .AsNoTracking()
            .Where(n => n.TopologyTemplateId == source.Id)
            .OrderBy(n => n.DisplayOrder)
            .ToListAsync(ct);
        var sourceFlags = await db.PenetrationFlagTemplates
            .AsNoTracking()
            .Where(f => f.TopologyTemplateId == source.Id)
            .OrderBy(f => f.Stage)
            .ToListAsync(ct);

        if (sourceNodes.Count == 0 || sourceFlags.Count == 0)
            throw new InvalidOperationException("penetration_topology_not_configured");

        var now = DateTime.UtcNow;
        var topology = new PenetrationTopology
        {
            Id = Guid.NewGuid(),
            CompetitionId = challenge.CompetitionId,
            ChallengeId = challengeId,
            Name = source.Name,
            Description = source.Description,
            NetworkConfigJson = source.NetworkConfigJson,
            EntryConfigJson = source.EntryConfigJson,
            HealthcheckConfigJson = source.HealthcheckConfigJson,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var nodeIdMap = new Dictionary<Guid, Guid>();
        var nodes = sourceNodes.Select(node =>
        {
            var id = Guid.NewGuid();
            nodeIdMap[node.Id] = id;
            return new PenetrationNode
            {
                Id = id,
                CompetitionId = challenge.CompetitionId,
                TopologyId = topology.Id,
                Name = node.Name,
                Role = node.Role,
                Image = node.Image,
                Command = node.Command,
                EntrypointJson = node.EntrypointJson,
                EnvironmentJson = node.EnvironmentJson,
                PortsJson = node.PortsJson,
                VolumesJson = node.VolumesJson,
                NetworksJson = node.NetworksJson,
                DependsOnJson = node.DependsOnJson,
                IsEntry = node.IsEntry,
                IsInternal = node.IsInternal,
                ResourceLimitJson = node.ResourceLimitJson,
                HealthcheckJson = node.HealthcheckJson,
                DisplayOrder = node.DisplayOrder,
                CreatedAt = now,
                UpdatedAt = now,
            };
        }).ToList();

        var flags = sourceFlags.Select(flag => new PenetrationFlag
        {
            Id = Guid.NewGuid(),
            CompetitionId = challenge.CompetitionId,
            ChallengeId = challengeId,
            TopologyId = topology.Id,
            NodeId = flag.NodeTemplateId.HasValue && nodeIdMap.TryGetValue(flag.NodeTemplateId.Value, out var nodeId) ? nodeId : null,
            Name = flag.Name,
            Stage = flag.Stage,
            ValueSecret = flag.ValueSecret,
            ValueHash = flag.ValueHash,
            Score = flag.Score,
            IsDynamic = flag.IsDynamic,
            Visible = flag.Visible,
            InjectionType = flag.InjectionType,
            InjectionKey = flag.InjectionKey,
            HintAfterSolved = flag.HintAfterSolved,
            SolvedCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
        }).ToList();

        challenge.PenetrationConfigJson = string.IsNullOrWhiteSpace(template.PenetrationConfigJson)
            ? "{}"
            : template.PenetrationConfigJson;
        challenge.DeploymentType = ChallengeDeploymentType.DynamicContainer;
        challenge.ContainerMode = ChallengeContainerMode.DockerCompose;

        db.PenetrationTopologies.Add(topology);
        db.PenetrationNodes.AddRange(nodes);
        db.PenetrationFlags.AddRange(flags);
        await db.SaveChangesAsync(ct);

        var dto = ToDto(topology, nodes, flags);
        dto.Config = ReadConfig(challenge);
        return dto;
    }

    public async Task<PenetrationTopologyDto> GetCompetitionTopologyAsync(Guid competitionId, Guid challengeId, CancellationToken ct)
    {
        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CompetitionId == competitionId && c.Id == challengeId, ct);
        var topology = await db.PenetrationTopologies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.CompetitionId == competitionId && t.ChallengeId == challengeId, ct);
        if (topology is null) return new PenetrationTopologyDto { Code = "not_configured" };

        var nodes = await db.PenetrationNodes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(n => n.CompetitionId == competitionId && n.TopologyId == topology.Id)
            .OrderBy(n => n.DisplayOrder)
            .ThenBy(n => n.Name)
            .ToListAsync(ct);
        var flags = await db.PenetrationFlags
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f => f.CompetitionId == competitionId && f.TopologyId == topology.Id)
            .OrderBy(f => f.Stage)
            .ToListAsync(ct);

        var dto = ToDto(topology, nodes, flags);
        dto.Config = ReadConfigJson(challenge?.PenetrationConfigJson);
        return dto;
    }

    public async Task<PenetrationTopologyDto> SaveCompetitionTopologyAsync(
        Guid competitionId,
        Guid challengeId,
        PenetrationTopologyDocument document,
        CancellationToken ct)
    {
        var activeInstances = await db.TeamChallengeInstances
            .IgnoreQueryFilters()
            .AnyAsync(i =>
                i.CompetitionId == competitionId &&
                i.ChallengeId == challengeId &&
                (i.Status == PenetrationInstanceStatus.Running ||
                 i.Status == PenetrationInstanceStatus.Starting ||
                 i.Status == PenetrationInstanceStatus.Resetting),
                ct);
        if (activeInstances) throw new InvalidOperationException("active_instances_exist");

        var challenge = await db.Challenges.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == challengeId && c.CompetitionId == competitionId, ct)
            ?? throw new InvalidOperationException("challenge_not_found");
        EnsurePenetrationType(challenge.TypeId);
        Validate(document);

        var now = DateTime.UtcNow;
        var existing = await db.PenetrationTopologies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.CompetitionId == competitionId && t.ChallengeId == challengeId, ct);
        var existingFlags = existing is null
            ? new Dictionary<Guid, PenetrationFlag>()
            : await db.PenetrationFlags
                .IgnoreQueryFilters()
                .Where(f => f.CompetitionId == competitionId && f.TopologyId == existing.Id)
                .ToDictionaryAsync(f => f.Id, ct);

        if (existing is not null)
        {
            db.PenetrationNodes.RemoveRange(await db.PenetrationNodes.IgnoreQueryFilters().Where(n => n.CompetitionId == competitionId && n.TopologyId == existing.Id).ToListAsync(ct));
            db.PenetrationFlags.RemoveRange(await db.PenetrationFlags.IgnoreQueryFilters().Where(f => f.CompetitionId == competitionId && f.TopologyId == existing.Id).ToListAsync(ct));
        }

        var topology = existing ?? new PenetrationTopology
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            CreatedAt = now,
        };

        topology.Name = Clean(document.Name) ?? challenge.Title;
        topology.Description = Clean(document.Description);
        topology.NetworkConfigJson = JsonOrDefault(document.NetworkConfig, "{}");
        topology.EntryConfigJson = JsonOrDefault(document.EntryConfig, "{}");
        topology.HealthcheckConfigJson = JsonOrDefault(document.HealthcheckConfig, "{}");
        topology.UpdatedAt = now;
        if (existing is null) db.PenetrationTopologies.Add(topology);

        challenge.PenetrationConfigJson = JsonSerializer.Serialize(document.Config ?? new PenetrationRuntimeConfig(), JsonOptions);

        var nodeMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var nodes = document.Nodes.Select((node, index) =>
        {
            var id = Guid.NewGuid();
            nodeMap[node.Name.Trim()] = id;
            return new PenetrationNode
            {
                Id = id,
                CompetitionId = competitionId,
                TopologyId = topology.Id,
                Name = node.Name.Trim(),
                Role = Clean(node.Role) ?? string.Empty,
                Image = node.Image.Trim(),
                Command = Clean(node.Command),
                EntrypointJson = JsonOrDefault(node.Entrypoint, "[]"),
                EnvironmentJson = JsonOrDefault(node.Environment, "{}"),
                PortsJson = JsonOrDefault(node.Ports, "[]"),
                VolumesJson = JsonOrDefault(node.Volumes, "[]"),
                NetworksJson = JsonOrDefault(node.Networks, "[]"),
                DependsOnJson = JsonOrDefault(node.DependsOn, "[]"),
                IsEntry = node.IsEntry,
                IsInternal = node.IsEntry ? node.IsInternal : true,
                ResourceLimitJson = JsonOrDefault(node.ResourceLimit, "{}"),
                HealthcheckJson = JsonOrDefault(node.Healthcheck, "{}"),
                DisplayOrder = node.DisplayOrder == 0 ? index + 1 : node.DisplayOrder,
                CreatedAt = now,
                UpdatedAt = now,
            };
        }).ToList();

        var flags = document.Flags.Select(flag =>
        {
            var nodeId = ResolveNodeId(flag.NodeId, flag.NodeName, nodeMap);
            existingFlags.TryGetValue(flag.Id ?? Guid.Empty, out var oldFlag);
            var secret = flag.IsDynamic ? null : Clean(flag.ValueSecret) ?? oldFlag?.ValueSecret;
            return new PenetrationFlag
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                ChallengeId = challengeId,
                TopologyId = topology.Id,
                NodeId = nodeId,
                Name = string.IsNullOrWhiteSpace(flag.Name) ? $"Stage {flag.Stage}" : flag.Name.Trim(),
                Stage = flag.Stage,
                ValueSecret = secret,
                ValueHash = secret is null ? oldFlag?.ValueHash : HashSecret(secret),
                Score = flag.Score,
                IsDynamic = flag.IsDynamic,
                Visible = flag.Visible,
                InjectionType = flag.InjectionType,
                InjectionKey = Clean(flag.InjectionKey),
                HintAfterSolved = Clean(flag.HintAfterSolved),
                CreatedAt = now,
                UpdatedAt = now,
            };
        }).ToList();

        db.PenetrationNodes.AddRange(nodes);
        db.PenetrationFlags.AddRange(flags);
        await db.SaveChangesAsync(ct);

        var dto = ToDto(topology, nodes, flags);
        dto.Config = ReadConfig(challenge);
        return dto;
    }

    public static PenetrationRuntimeConfig ReadConfig(Challenge challenge)
        => ReadConfigJson(challenge.PenetrationConfigJson);

    private static PenetrationRuntimeConfig ReadConfigJson(string? configJson)
        => string.IsNullOrWhiteSpace(configJson)
            ? new PenetrationRuntimeConfig()
            : JsonSerializer.Deserialize<PenetrationRuntimeConfig>(configJson, JsonOptions) ?? new PenetrationRuntimeConfig();

    public static string HashSecret(string secret)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void Validate(PenetrationTopologyDocument document)
    {
        if (document.Nodes.Count == 0) throw new InvalidOperationException("penetration_nodes_required");
        if (!document.Nodes.Any(n => n.IsEntry)) throw new InvalidOperationException("penetration_entry_node_required");
        if (document.Flags.Count == 0) throw new InvalidOperationException("penetration_flags_required");

        var nodeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in document.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Name) || !ServiceNamePattern.IsMatch(node.Name.Trim()))
                throw new InvalidOperationException("invalid_node_name");
            if (!nodeNames.Add(node.Name.Trim()))
                throw new InvalidOperationException("duplicate_node_name");
            if (string.IsNullOrWhiteSpace(node.Image))
                throw new InvalidOperationException("node_image_required");

            var joined = string.Join('\n',
                node.Command,
                JsonOrDefault(node.Entrypoint, "[]"),
                JsonOrDefault(node.Environment, "{}"),
                JsonOrDefault(node.Ports, "[]"),
                JsonOrDefault(node.Volumes, "[]"),
                JsonOrDefault(node.Networks, "[]"),
                JsonOrDefault(node.DependsOn, "[]"),
                JsonOrDefault(node.ResourceLimit, "{}"));
            if (ForbiddenTokens.Any(token => joined.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
                joined.Contains("..", StringComparison.Ordinal))
                throw new InvalidOperationException("forbidden_container_directive");
            if (!node.IsEntry && JsonOrDefault(node.Ports, "[]").Contains(':', StringComparison.Ordinal))
                throw new InvalidOperationException("internal_node_cannot_publish_ports");
        }

        var stages = new HashSet<int>();
        var staticSecrets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var flag in document.Flags)
        {
            if (flag.Stage <= 0 || !stages.Add(flag.Stage))
                throw new InvalidOperationException("invalid_or_duplicate_stage");
            if (flag.Score <= 0)
                throw new InvalidOperationException("flag_score_required");
            if (!string.IsNullOrWhiteSpace(flag.NodeName) && !nodeNames.Contains(flag.NodeName.Trim()))
                throw new InvalidOperationException("flag_node_not_found");
            if (flag.InjectionType == PenetrationFlagInjectionType.File)
                throw new InvalidOperationException("file_injection_not_supported");
            if (flag.IsDynamic && string.IsNullOrWhiteSpace(flag.InjectionKey))
                throw new InvalidOperationException("dynamic_flag_injection_key_required");
            if (!flag.IsDynamic)
            {
                if (string.IsNullOrWhiteSpace(flag.ValueSecret) && flag.Id is null)
                    throw new InvalidOperationException("static_flag_secret_required");
                if (!string.IsNullOrWhiteSpace(flag.ValueSecret) && !staticSecrets.Add(flag.ValueSecret.Trim()))
                    throw new InvalidOperationException("duplicate_static_flag_secret");
            }
        }
    }

    private static Guid? ResolveNodeId(Guid? requestedNodeId, string? nodeName, Dictionary<string, Guid> nodeMap)
    {
        if (!string.IsNullOrWhiteSpace(nodeName) && nodeMap.TryGetValue(nodeName.Trim(), out var id))
            return id;
        return requestedNodeId;
    }

    private static PenetrationTopologyDto ToDto(
        PenetrationTopologyTemplate topology,
        IReadOnlyList<PenetrationNodeTemplate> nodes,
        IReadOnlyList<PenetrationFlagTemplate> flags)
    {
        var nodeNames = nodes.ToDictionary(n => n.Id, n => n.Name);
        return new PenetrationTopologyDto
        {
            Id = topology.Id,
            Name = topology.Name,
            Description = topology.Description,
            NetworkConfig = ParseJson(topology.NetworkConfigJson),
            EntryConfig = ParseJson(topology.EntryConfigJson),
            HealthcheckConfig = ParseJson(topology.HealthcheckConfigJson),
            Nodes = nodes.Select(ToDto).ToList(),
            Flags = flags.Select(flag => ToDto(flag, flag.NodeTemplateId, nodeNames)).ToList(),
        };
    }

    private static PenetrationTopologyDto ToDto(
        PenetrationTopology topology,
        IReadOnlyList<PenetrationNode> nodes,
        IReadOnlyList<PenetrationFlag> flags)
    {
        var nodeNames = nodes.ToDictionary(n => n.Id, n => n.Name);
        return new PenetrationTopologyDto
        {
            Id = topology.Id,
            Name = topology.Name,
            Description = topology.Description,
            NetworkConfig = ParseJson(topology.NetworkConfigJson),
            EntryConfig = ParseJson(topology.EntryConfigJson),
            HealthcheckConfig = ParseJson(topology.HealthcheckConfigJson),
            Nodes = nodes.Select(ToDto).ToList(),
            Flags = flags.Select(flag => ToDto(flag, flag.NodeId, nodeNames)).ToList(),
        };
    }

    private static PenetrationNodeDto ToDto(PenetrationNodeTemplate node) => new()
    {
        Id = node.Id,
        Name = node.Name,
        Role = node.Role,
        Image = node.Image,
        Command = node.Command,
        Entrypoint = ParseJson(node.EntrypointJson),
        Environment = ParseJson(node.EnvironmentJson),
        Ports = ParseJson(node.PortsJson),
        Volumes = ParseJson(node.VolumesJson),
        Networks = ParseJson(node.NetworksJson),
        DependsOn = ParseJson(node.DependsOnJson),
        IsEntry = node.IsEntry,
        IsInternal = node.IsInternal,
        ResourceLimit = ParseJson(node.ResourceLimitJson),
        Healthcheck = ParseJson(node.HealthcheckJson),
        DisplayOrder = node.DisplayOrder,
    };

    private static PenetrationNodeDto ToDto(PenetrationNode node) => new()
    {
        Id = node.Id,
        Name = node.Name,
        Role = node.Role,
        Image = node.Image,
        Command = node.Command,
        Entrypoint = ParseJson(node.EntrypointJson),
        Environment = ParseJson(node.EnvironmentJson),
        Ports = ParseJson(node.PortsJson),
        Volumes = ParseJson(node.VolumesJson),
        Networks = ParseJson(node.NetworksJson),
        DependsOn = ParseJson(node.DependsOnJson),
        IsEntry = node.IsEntry,
        IsInternal = node.IsInternal,
        ResourceLimit = ParseJson(node.ResourceLimitJson),
        Healthcheck = ParseJson(node.HealthcheckJson),
        DisplayOrder = node.DisplayOrder,
    };

    private static PenetrationFlagDto ToDto(PenetrationFlagTemplate flag, Guid? nodeId, IReadOnlyDictionary<Guid, string> nodeNames) => new()
    {
        Id = flag.Id,
        NodeId = nodeId,
        NodeName = nodeId.HasValue && nodeNames.TryGetValue(nodeId.Value, out var name) ? name : null,
        Name = flag.Name,
        Stage = flag.Stage,
        Score = flag.Score,
        IsDynamic = flag.IsDynamic,
        Visible = flag.Visible,
        InjectionType = flag.InjectionType.ToString(),
        InjectionKey = flag.InjectionKey,
        HintAfterSolved = flag.HintAfterSolved,
        HasSecret = !string.IsNullOrWhiteSpace(flag.ValueSecret) || !string.IsNullOrWhiteSpace(flag.ValueHash),
        SolveCount = flag.SolvedCount,
    };

    private static PenetrationFlagDto ToDto(PenetrationFlag flag, Guid? nodeId, IReadOnlyDictionary<Guid, string> nodeNames) => new()
    {
        Id = flag.Id,
        NodeId = nodeId,
        NodeName = nodeId.HasValue && nodeNames.TryGetValue(nodeId.Value, out var name) ? name : null,
        Name = flag.Name,
        Stage = flag.Stage,
        Score = flag.Score,
        IsDynamic = flag.IsDynamic,
        Visible = flag.Visible,
        InjectionType = flag.InjectionType.ToString(),
        InjectionKey = flag.InjectionKey,
        HintAfterSolved = flag.HintAfterSolved,
        HasSecret = !string.IsNullOrWhiteSpace(flag.ValueSecret) || !string.IsNullOrWhiteSpace(flag.ValueHash),
        SolveCount = flag.SolvedCount,
    };

    private static object? ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<object>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string JsonOrDefault(JsonElement? element, string fallback)
        => element.HasValue && element.Value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
            ? element.Value.GetRawText()
            : fallback;

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsurePenetrationType(string typeId)
    {
        if (!string.Equals(typeId, PenetrationConstants.TypeId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("not_penetration_challenge");
    }
}
