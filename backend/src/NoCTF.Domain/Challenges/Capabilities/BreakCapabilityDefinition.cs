using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Challenges.Capabilities;

public enum FlagDeliveryKind : short { RunnerCommand, BusinessChecker }

[PersistentDiscriminator("break")]
public sealed class BreakCapabilityDefinition() : ChallengeCapabilityDefinition(ChallengeCapabilityKind.Break)
{
    public bool HasFlagTemplate { get; set; }
    public FlagTemplateValue FlagTemplate { get; set; } = new();
    [MaxLength(8192)] public string? InjectionCommand { get; set; }
    public int InjectionTimeoutSeconds { get; set; } = 30;
    [MaxLength(63)] public string? InjectionServiceName { get; set; }
    public List<FlagStoreDefinition> FlagStores { get; set; } = [];
    public List<FlagEnvironmentTarget> EnvironmentTargets { get; set; } = [];
}

public sealed class FlagEnvironmentTarget
{
    public Guid ChallengeId { get; set; }
    public int Position { get; set; }
    [MaxLength(63)] public string ServiceName { get; set; } = string.Empty;
    [MaxLength(256)] public string VariableName { get; set; } = string.Empty;
}

public sealed class FlagStoreDefinition
{
    public Guid ChallengeId { get; set; }
    public Guid Id { get; set; }
    public int Position { get; set; }
    [MaxLength(64)] public string Name { get; set; } = "main";
    [MaxLength(63)] public string? TargetServiceName { get; set; }
    public FlagDeliveryKind DeliveryKind { get; set; }
    [MaxLength(8192)] public string? InjectionCommand { get; set; }
    public int InjectionTimeoutSeconds { get; set; } = 30;
    public bool PreservesHistoricalFlags { get; set; } = true;
}
