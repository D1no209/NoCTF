using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Challenges.Capabilities;

[PersistentDiscriminator("control")]
public sealed class ControlCapabilityDefinition() : ChallengeCapabilityDefinition(ChallengeCapabilityKind.Control)
{
    [MaxLength(512)] public string? Image { get; set; }
    [MaxLength(63)] public string? ServiceName { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
    public List<ControlCommandArgument> Command { get; set; } = [];
    public List<ControlEnvironmentVariable> Environment { get; set; } = [];
}

public sealed class ControlCommandArgument
{
    public Guid ChallengeId { get; set; }
    public int Position { get; set; }
    [MaxLength(4096)] public string Value { get; set; } = string.Empty;
}

public sealed class ControlEnvironmentVariable
{
    public Guid ChallengeId { get; set; }
    [MaxLength(256)] public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
