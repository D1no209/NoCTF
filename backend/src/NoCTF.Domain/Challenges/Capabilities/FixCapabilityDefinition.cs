using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Challenges.Capabilities;

[PersistentDiscriminator("fix")]
public sealed class FixCapabilityDefinition() : ChallengeCapabilityDefinition(ChallengeCapabilityKind.Fix)
{
    [MaxLength(63)] public string TargetServiceName { get; set; } = string.Empty;
    [MaxLength(1024)] public string Entrypoint { get; set; } = "fix.sh";
    public int TimeoutSeconds { get; set; } = 60;
    public long MaximumUploadBytes { get; set; } = 256L * 1024 * 1024;
    public List<FixCommandArgument> Command { get; set; } = [];
}

public sealed class FixCommandArgument
{
    public Guid ChallengeId { get; set; }
    public int Position { get; set; }
    [MaxLength(4096)] public string Value { get; set; } = string.Empty;
}
