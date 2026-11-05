using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Challenges.Capabilities;

public enum ControlProtocol : short { HttpTeamIdentifier }

[PersistentDiscriminator("control")]
public sealed class ControlCapabilityDefinition() : ChallengeCapabilityDefinition(ChallengeCapabilityKind.Control)
{
    public ControlProtocol Protocol { get; set; }
    [MaxLength(2048)] public string UrlTemplate { get; set; } = string.Empty;
    [MaxLength(63)] public string? ServiceName { get; set; }
    public int? ContainerPort { get; set; }
    public string? VmId { get; set; }
    public int? GuestPort { get; set; }
    public int TimeoutSeconds { get; set; } = 10;
}
