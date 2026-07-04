using System.Text.Json;
using System.Text.Json.Serialization;
using NoCTF.Core;

namespace NoCTF.Plugins.Penetration;

public sealed class PenetrationTopologyDocument
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public JsonElement? NetworkConfig { get; set; }
    public JsonElement? EntryConfig { get; set; }
    public JsonElement? HealthcheckConfig { get; set; }
    public PenetrationRuntimeConfig? Config { get; set; }
    public List<PenetrationNodeDocument> Nodes { get; set; } = [];
    public List<PenetrationFlagDocument> Flags { get; set; } = [];
}

public sealed class PenetrationRuntimeConfig
{
    public bool AllowReset { get; set; } = true;
    public string InstanceMode { get; set; } = "team";
    public int MaxResetCount { get; set; } = 3;
    public string ResourceLimitJson { get; set; } = "{}";
    public bool VisibleEntryAfterStart { get; set; } = true;
    public int InstanceTtlSeconds { get; set; } = 7200;
    public int ActionCooldownSeconds { get; set; } = 5;
}

public sealed class PenetrationNodeDocument
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string? Command { get; set; }
    public JsonElement? Entrypoint { get; set; }
    public JsonElement? Environment { get; set; }
    public JsonElement? Ports { get; set; }
    public JsonElement? Volumes { get; set; }
    public JsonElement? Networks { get; set; }
    public JsonElement? DependsOn { get; set; }
    public bool IsEntry { get; set; }
    public bool IsInternal { get; set; } = true;
    public JsonElement? ResourceLimit { get; set; }
    public JsonElement? Healthcheck { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class PenetrationFlagDocument
{
    public Guid? Id { get; set; }
    public Guid? NodeId { get; set; }
    public string? NodeName { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Stage { get; set; }
    public string? ValueSecret { get; set; }
    public int Score { get; set; } = 100;
    public bool IsDynamic { get; set; }
    public bool Visible { get; set; } = true;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PenetrationFlagInjectionType InjectionType { get; set; } = PenetrationFlagInjectionType.EnvironmentVariable;
    public string? InjectionKey { get; set; }
    public string? HintAfterSolved { get; set; }
}

public sealed class PenetrationTopologyDto
{
    public Guid? Id { get; set; }
    public string Code { get; set; } = "ok";
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public object? NetworkConfig { get; set; }
    public object? EntryConfig { get; set; }
    public object? HealthcheckConfig { get; set; }
    public PenetrationRuntimeConfig Config { get; set; } = new();
    public List<PenetrationNodeDto> Nodes { get; set; } = [];
    public List<PenetrationFlagDto> Flags { get; set; } = [];
}

public sealed class PenetrationNodeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string? Command { get; set; }
    public object? Entrypoint { get; set; }
    public object? Environment { get; set; }
    public object? Ports { get; set; }
    public object? Volumes { get; set; }
    public object? Networks { get; set; }
    public object? DependsOn { get; set; }
    public bool IsEntry { get; set; }
    public bool IsInternal { get; set; }
    public object? ResourceLimit { get; set; }
    public object? Healthcheck { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class PenetrationFlagDto
{
    public Guid Id { get; set; }
    public Guid? NodeId { get; set; }
    public string? NodeName { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Stage { get; set; }
    public int Score { get; set; }
    public bool IsDynamic { get; set; }
    public bool Visible { get; set; }
    public string InjectionType { get; set; } = "EnvironmentVariable";
    public string? InjectionKey { get; set; }
    public string? HintAfterSolved { get; set; }
    public bool HasSecret { get; set; }
    public bool Solved { get; set; }
    public DateTime? SolvedAt { get; set; }
    public int SolveCount { get; set; }
}

public sealed class PenetrationInstanceDto
{
    public Guid? Id { get; set; }
    public string Status { get; set; } = "None";
    public string? EntryHost { get; set; }
    public int? EntryPort { get; set; }
    public string? EntryUrl { get; set; }
    public int ResetCount { get; set; }
    public int ResetLimit { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? CooldownUntil { get; set; }
    public DateTime ServerTime { get; set; }
    public string? LastError { get; set; }
    public string[] ContainerIds { get; set; } = [];
    public Dictionary<int, int> Ports { get; set; } = [];
}

public sealed class PenetrationChallengeDetailDto
{
    public Guid CompetitionId { get; set; }
    public Guid ChallengeId { get; set; }
    public string AuthorizationScope { get; set; } = string.Empty;
    public PenetrationTopologyDto? Topology { get; set; }
    public PenetrationInstanceDto Instance { get; set; } = new();
    public int TotalStageCount { get; set; }
    public int SolvedStageCount { get; set; }
    public int TotalScore { get; set; }
}

public sealed class PenetrationCloneRequest
{
    public Guid TemplateId { get; set; }
    public Guid ChallengeId { get; set; }
}
