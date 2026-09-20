using Microsoft.Extensions.Options;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Application.Runtime.Capacity;

namespace NoCTF.Runner.Composition;

public sealed class RunnerOptions
{
    public const string SectionName = "Runner";

    public string Id { get; set; } = string.Empty;
    public string Pool { get; set; } = string.Empty;
    public RuntimeProvider? Provider { get; set; }
    // Rolling-compatibility only. Actual-usage runners ignore this configured ceiling.
    public RunnerCapacityOptions Capacity { get; set; } = new();
    public RunnerHeartbeatOptions Heartbeat { get; set; } = new();
    public RunnerCleanupOptions Cleanup { get; set; } = new();
    public RunnerAdmissionOptions Admission { get; set; } = new();
    public int ProviderFailureHoldSeconds { get; set; } = 120;

    public RuntimeResourceLimits ResourceCapacity => new(
        Capacity.MemoryBytes,
        Capacity.NanoCpus,
        Capacity.PidsLimit);
}

public sealed class RunnerCapacityOptions
{
    public long MemoryBytes { get; set; }
    public long NanoCpus { get; set; }
    public long PidsLimit { get; set; }
}

public sealed class RunnerCleanupOptions
{
    public int GracefulStopSeconds { get; set; } = 2;
    public int ForceDeleteTimeoutSeconds { get; set; } = 8;
    public int NetworkCleanupTimeoutSeconds { get; set; } = 5;
    public int VerificationTimeoutSeconds { get; set; } = 3;

    public RuntimeTerminationPolicy ToPolicy() => new(
        TimeSpan.FromSeconds(GracefulStopSeconds),
        TimeSpan.FromSeconds(ForceDeleteTimeoutSeconds),
        TimeSpan.FromSeconds(NetworkCleanupTimeoutSeconds),
        TimeSpan.FromSeconds(VerificationTimeoutSeconds));
}

public sealed class RunnerHeartbeatOptions
{
    public int IntervalSeconds { get; set; }
    public int TtlSeconds { get; set; }

    public TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);
    public TimeSpan Ttl => TimeSpan.FromSeconds(TtlSeconds);
}

public sealed class RunnerOptionsValidator : IValidateOptions<RunnerOptions>
{
    public ValidateOptionsResult Validate(string? name, RunnerOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Id) || options.Id.Length > 128)
            failures.Add("Runner:Id must contain 1..128 characters.");
        if (string.IsNullOrWhiteSpace(options.Pool) || options.Pool.Length > 256)
            failures.Add("Runner:Pool must contain 1..256 characters.");
        if (options.Provider is null)
            failures.Add("Runner:Provider must be Docker, Kubernetes, or Libvirt.");
        if (options.Heartbeat.IntervalSeconds <= 0
            || options.Heartbeat.TtlSeconds <= options.Heartbeat.IntervalSeconds)
            failures.Add("Runner heartbeat TTL must be greater than its positive interval.");
        if (options.ProviderFailureHoldSeconds <= 0)
            failures.Add("Runner provider failure hold must be a positive number of seconds.");
        if (!options.Admission.IsValid())
            failures.Add("Runner admission thresholds, sampling and concurrency must be valid.");
        if (options.Cleanup.GracefulStopSeconds <= 0
            || options.Cleanup.ForceDeleteTimeoutSeconds <= 0
            || options.Cleanup.NetworkCleanupTimeoutSeconds <= 0
            || options.Cleanup.VerificationTimeoutSeconds <= 0)
        {
            failures.Add("Runner cleanup timeouts must be positive numbers of seconds.");
        }
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
