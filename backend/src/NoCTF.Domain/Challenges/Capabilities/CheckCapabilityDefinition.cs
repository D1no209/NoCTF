using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Challenges.Capabilities;

public enum CheckItemKind : short { Connection, Functionality, Vulnerability, FlagWrite, CurrentFlagRead, HistoricalFlagRead }

[PersistentDiscriminator("check")]
public sealed class CheckCapabilityDefinition() : ChallengeCapabilityDefinition(ChallengeCapabilityKind.Check)
{
    [MaxLength(512)] public string Image { get; set; } = string.Empty;
    [MaxLength(63)] public string TargetServiceName { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public int ReadyTimeoutSeconds { get; set; } = 30;
    public bool AcceptsFixInput { get; set; }
    public List<CheckCommandArgument> Command { get; set; } = [];
    public List<CheckEnvironmentVariable> Environment { get; set; } = [];
    public List<CheckRequirement> Requirements { get; set; } = [];
}

public sealed class CheckCommandArgument
{
    public Guid ChallengeId { get; set; }
    public int Position { get; set; }
    [MaxLength(4096)] public string Value { get; set; } = string.Empty;
}

public sealed class CheckEnvironmentVariable
{
    public Guid ChallengeId { get; set; }
    [MaxLength(256)] public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class CheckRequirement
{
    public Guid ChallengeId { get; set; }
    public CheckItemKind Kind { get; set; }
}
