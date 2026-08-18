using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runner.Composition;

public sealed class RunnerAvailabilityOptions
{
    public string RunnerId { get; set; } = string.Empty;
    public string RunnerPool { get; set; } = string.Empty;
    public RuntimeProvider? Provider { get; set; }
    public long MemoryBytes { get; set; }
    public long NanoCpus { get; set; }
    public long PidsLimit { get; set; }
    public int HeartbeatIntervalSeconds { get; set; }
    public int HeartbeatTtlSeconds { get; set; }
    public int ProviderFailureHoldSeconds { get; set; } = 120;

    public RuntimeResourceLimits Capacity => new(MemoryBytes, NanoCpus, PidsLimit);
    public TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(HeartbeatIntervalSeconds);
    public TimeSpan HeartbeatTtl => TimeSpan.FromSeconds(HeartbeatTtlSeconds);
}
