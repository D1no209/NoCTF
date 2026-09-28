using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Shared;
using System.ComponentModel.DataAnnotations.Schema;

namespace NoCTF.Domain.Challenges;

[PersistentHierarchy]
public abstract class ChallengeDefinition
{
    protected ChallengeDefinition(GameMode mode) => Mode = mode;
    public Guid ChallengeId { get; set; }
    public GameMode Mode { get; private set; }
    public ChallengeRuntimeTemplateEntity? Runtime { get; set; }
    public string? PatchEntrypoint { get; set; }
    public int? PatchTimeoutSeconds { get; set; }
    public int? ReadyTimeoutSeconds { get; set; }
    public long? MaximumPatchUploadBytes { get; set; }
    public bool CheckerFixInput { get; set; }
    public bool CheckerAllowRoot { get; set; }
    public bool HasFlagTemplate { get; set; }
    public FlagTemplateValue FlagTemplate { get; set; } = new();
    public ChallengeCheckerDefinition? Checker { get; set; }
    public List<ChallengeDefinitionStringItem> StringItems { get; set; } = [];
}

[PersistentDiscriminator("ctf")]
public sealed class CtfChallengeDefinition() : ChallengeDefinition(GameMode.Ctf)
{
    public CtfInteractionKind InteractionKind { get; set; }
}

[PersistentDiscriminator("awd")]
public sealed class AwdChallengeDefinition() : ChallengeDefinition(GameMode.Awd)
{
    public string? FlagInjectionCommand { get; set; }
    public int? FlagInjectionTimeoutSeconds { get; set; }
    public string? FlagInjectionServiceName { get; set; }
}

[PersistentDiscriminator("awdp")]
public sealed class AwdpChallengeDefinition() : ChallengeDefinition(GameMode.Awdp);

[PersistentDiscriminator("koh")]
public sealed class KohChallengeDefinition() : ChallengeDefinition(GameMode.Koh);

public enum ChallengeDefinitionStringKind : short
{
    PatchCommand,
    CheckerCommand,
    CheckerEnvironment
}

public sealed class ChallengeDefinitionStringItem
{
    public Guid ChallengeId { get; set; }
    public ChallengeDefinitionStringKind Kind { get; set; }
    public int Position { get; set; }
    public string? Key { get; set; }
    public string Value { get; set; } = string.Empty;
}

public sealed class ChallengeCheckerDefinition
{
    public Guid ChallengeId { get; set; }
    public string Image { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; }
    public string? TargetServiceName { get; set; }
}

[PersistentHierarchy]
public abstract class ChallengeRuntimeTemplateEntity
{
    protected ChallengeRuntimeTemplateEntity(RuntimeKind runtimeKind) => RuntimeKind = runtimeKind;
    public Guid ChallengeId { get; set; }
    public RuntimeKind RuntimeKind { get; private set; }
    public PersistedRuntimeAllocation Allocation { get; set; }
    public RuntimeResourceLimitsValue Limits { get; set; } = new();
    public bool HasExplicitLimits { get; set; }
    public int? TtlSeconds { get; set; }
    public int? OperationTimeoutSeconds { get; set; }
    public PersistedRuntimeFlagSource FlagSource { get; set; }
    public PersistedRuntimeEgressPolicy EgressPolicy { get; set; }
    public List<ChallengeRuntimeUrlBinding> UrlBindings { get; set; } = [];
    public List<ChallengeRuntimeKeyValue> KeyValues { get; set; } = [];
    public List<ChallengeRuntimeCommandItem> CommandItems { get; set; } = [];
}

public enum PersistedRuntimeAllocation : short { Shared, PerTeam }
public enum PersistedRuntimeFlagSource : short { Static, PerTeam, AwdRotation }
public enum PersistedRuntimeEgressPolicy : short { Isolated, InternetOnly }
public enum PersistedRuntimeExposure : short { OwnerOnly, Participants }

public sealed class RuntimeResourceLimitsValue
{
    public long MemoryBytes { get; set; }
    public long NanoCpus { get; set; }
    public long PidsLimit { get; set; }
}

public sealed class ContainerSecurityPolicyValue
{
    public bool NoNewPrivileges { get; set; }
    public bool ReadonlyRootfs { get; set; }
    public bool RunAsNonRoot { get; set; }
}

[PersistentDiscriminator("container")]
public sealed class ContainerChallengeRuntimeTemplate()
    : ChallengeRuntimeTemplateEntity(RuntimeKind.Container)
{
    public string Image { get; set; } = string.Empty;
    public string? FlagEnvironmentVariableName { get; set; }
    public ContainerSecurityPolicyValue Security { get; set; } = new();
    public List<ChallengeRuntimeCapability> Capabilities { get; set; } = [];
    public List<ChallengeRuntimePortMapping> PortMappings { get; set; } = [];
    public List<ChallengeRuntimeInternalPort> InternalPorts { get; set; } = [];
}

[PersistentDiscriminator("compose")]
public sealed class ComposeChallengeRuntimeTemplate()
    : ChallengeRuntimeTemplateEntity(RuntimeKind.Compose)
{
    public string ComposeYaml { get; set; } = string.Empty;
    public List<ComposeServiceResource> ServiceResources { get; set; } = [];
}

[PersistentDiscriminator("ova")]
public sealed class OvaChallengeRuntimeTemplate()
    : ChallengeRuntimeTemplateEntity(RuntimeKind.OvaVm)
{
    public string OvaSourceUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

public enum ChallengeRuntimeKeyValueKind : short { Environment, Label, FlagEnvironmentVariable }

public sealed class ChallengeRuntimeKeyValue
{
    public Guid ChallengeId { get; set; }
    public ChallengeRuntimeKeyValueKind Kind { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class ChallengeRuntimeCommandItem
{
    public Guid ChallengeId { get; set; }
    public int Position { get; set; }
    public string Value { get; set; } = string.Empty;
}

public sealed class ChallengeRuntimeCapability
{
    public Guid ChallengeId { get; set; }
    public bool Add { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class ChallengeRuntimePortMapping
{
    public Guid ChallengeId { get; set; }
    public int ContainerPort { get; set; }
    public int HostPort { get; set; }
}

public sealed class ChallengeRuntimeInternalPort
{
    public Guid ChallengeId { get; set; }
    public int Port { get; set; }
}

public sealed class ComposeServiceResource
{
    public Guid ChallengeId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public RuntimeResourceLimitsValue Limits { get; set; } = new();
}

public sealed class ChallengeRuntimeUrlBinding
{
    public Guid ChallengeId { get; set; }
    public int Position { get; set; }
    public bool IsControlCheck { get; set; }
    public string UrlTemplate { get; set; } = string.Empty;
    public PersistedRuntimeExposure Exposure { get; set; }
    public int? ContainerPort { get; set; }
    public string? ServiceName { get; set; }
    public string? VmId { get; set; }
    public int? GuestPort { get; set; }
}

[PersistentHierarchy]
public abstract class CompetitionChallengeRules
{
    protected CompetitionChallengeRules(GameMode mode) => Mode = mode;
    public Guid CompetitionChallengeId { get; set; }
    public GameMode Mode { get; private set; }
    public ScoreCurveValue ScoreCurve { get; set; } = new();
    public bool HasScoreCurve { get; set; }
    public ScoreCurveValue BreakScoreCurve { get; set; } = new();
    public bool HasBreakScoreCurve { get; set; }
    public ScoreCurveValue FixScoreCurve { get; set; } = new();
    public bool HasFixScoreCurve { get; set; }
    public List<CompetitionChallengeBloodReward> BloodRewards { get; set; } = [];
    public int? MaxFlagAttempts { get; set; }
    public int? MaxPatchAttempts { get; set; }
    public int? MaxBreakSubmissions { get; set; }
    public int? MaxFixSubmissions { get; set; }
    public bool? RequireBreakBeforeFix { get; set; }
    public long? WrongSubmissionPenalty { get; set; }
    public long? FlagWrongPenalty { get; set; }
    public long? ExploitSucceededPenalty { get; set; }
    public long? ServiceAbnormalPenalty { get; set; }
    public AwdAttackRewardMode? AttackRewardMode { get; set; }
    public long? AttackPoints { get; set; }
    public long? VictimDefensePoolPoints { get; set; }
    public int? CheckerIntervalSeconds { get; set; }
    public long? ServiceHealthyPoints { get; set; }
    public long? ServiceUnhealthyPenalty { get; set; }
    public int? PollIntervalSeconds { get; set; }
    public long? ControlPointsPerInterval { get; set; }
    public CompetitionEvaluationDispatchMode? EvaluationDispatchMode { get; set; }
    public FlagTemplateValue FlagTemplate { get; set; } = new();
    public bool HasFlagTemplate { get; set; }
}

[PersistentDiscriminator("ctf")]
public sealed class CtfCompetitionChallengeRules() : CompetitionChallengeRules(GameMode.Ctf);
[PersistentDiscriminator("awd")]
public sealed class AwdCompetitionChallengeRules() : CompetitionChallengeRules(GameMode.Awd);
[PersistentDiscriminator("awdp")]
public sealed class AwdpCompetitionChallengeRules() : CompetitionChallengeRules(GameMode.Awdp);
[PersistentDiscriminator("koh")]
public sealed class KohCompetitionChallengeRules() : CompetitionChallengeRules(GameMode.Koh);

public sealed class CompetitionChallengeBloodReward
{
    public Guid CompetitionChallengeId { get; set; }
    public int Position { get; set; }
    public CompetitionBloodRewardPolicy Policy { get; set; }
    public decimal Value { get; set; }
}
