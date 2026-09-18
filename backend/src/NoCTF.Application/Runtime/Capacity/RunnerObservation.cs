using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Capacity;

public sealed record RunnerResourceObservation(
    string ResourceDomain,
    DateTimeOffset ObservedAt,
    long MemoryTotalBytes,
    long MemoryAvailableBytes,
    long NanoCpus,
    double CpuUsageRatio,
    long? PidsUsed,
    long PidsCapacity,
    long OomKills,
    bool ProviderPressure = false,
    bool PidPressureConditionAvailable = false);

public sealed record RunnerAdmissionSnapshot(
    RunnerAdmissionState State,
    RunnerAdmissionFailure? Failure,
    RunnerResourceObservation? Observation);

public sealed class RunnerAdmissionOptions
{
    public int SampleIntervalSeconds { get; set; } = 5;
    public int FreshnessSeconds { get; set; } = 15;
    public int CpuPressureWindowSeconds { get; set; } = 20;
    public double CpuHighRatio { get; set; } = .9;
    public double CpuRecoveryRatio { get; set; } = .8;
    public double MemoryLowRatio { get; set; } = .1;
    public double MemoryRecoveryRatio { get; set; } = .15;
    public double PidsHighRatio { get; set; } = .9;
    public double PidsRecoveryRatio { get; set; } = .8;
    public int HealthySamplesToResume { get; set; } = 3;
    public int MainStartupConcurrency { get; set; } = 2;
    public int AuxiliaryConcurrency { get; set; } = 1;
    public long AuxiliaryReservedMemoryBytes { get; set; } = 256 * 1024 * 1024;
    public long AuxiliaryReservedNanoCpus { get; set; } = 250_000_000;
    public long AuxiliaryReservedPids { get; set; } = 128;
    public string HostProcRoot { get; set; } = "/host/proc";
    public string HostCgroupRoot { get; set; } = "/host/sys/fs/cgroup";
    public string HostIdentityPath { get; set; } = "/host/etc/machine-id";

    public bool IsValid() => SampleIntervalSeconds > 0 && FreshnessSeconds > SampleIntervalSeconds
        && CpuPressureWindowSeconds > 0 && HealthySamplesToResume > 0
        && MainStartupConcurrency > 0 && AuxiliaryConcurrency > 0
        && CpuHighRatio is > 0 and <= 1 && CpuRecoveryRatio > 0 && CpuRecoveryRatio < CpuHighRatio
        && MemoryLowRatio > 0 && MemoryRecoveryRatio > MemoryLowRatio && MemoryRecoveryRatio < 1
        && PidsHighRatio is > 0 and <= 1 && PidsRecoveryRatio > 0 && PidsRecoveryRatio < PidsHighRatio
        && AuxiliaryReservedMemoryBytes > 0 && AuxiliaryReservedNanoCpus > 0 && AuxiliaryReservedPids > 0
        && !string.IsNullOrWhiteSpace(HostProcRoot) && !string.IsNullOrWhiteSpace(HostCgroupRoot)
        && !string.IsNullOrWhiteSpace(HostIdentityPath);
}

/// <summary>Pressure gates new admission; measured usage never rewrites allocation balances.</summary>
public sealed class RunnerPressurePolicy(RunnerAdmissionOptions options)
{
    private DateTimeOffset? cpuHighSince;
    private DateTimeOffset? oomUntil;
    private long? previousOomKills;
    private bool blocked;
    private int healthySamples;
    private DateTimeOffset? lastHealthyAt;

    public RunnerAdmissionSnapshot Evaluate(RunnerResourceObservation? sample, DateTimeOffset now)
    {
        if (sample is null || now - sample.ObservedAt > TimeSpan.FromSeconds(options.FreshnessSeconds)
            || sample.ObservedAt > now || sample.MemoryTotalBytes <= 0 || sample.NanoCpus <= 0
            || sample.PidsCapacity <= 0 || !double.IsFinite(sample.CpuUsageRatio))
        {
            healthySamples = 0;
            return new(RunnerAdmissionState.Starting, RunnerAdmissionFailure.ObservationStale, sample);
        }
        if (previousOomKills is long previous && sample.OomKills > previous)
            oomUntil = now.AddMinutes(1);
        previousOomKills = sample.OomKills;
        cpuHighSince = sample.CpuUsageRatio >= options.CpuHighRatio ? cpuHighSince ?? now : null;
        var memoryRatio = (double)sample.MemoryAvailableBytes / sample.MemoryTotalBytes;
        if (sample.PidsUsed is null && !sample.PidPressureConditionAvailable)
            return new(RunnerAdmissionState.Starting, RunnerAdmissionFailure.ObservationStale, sample);
        var pidsRatio = sample.PidsUsed is long pids ? (double)pids / sample.PidsCapacity : 0;
        var pressure = sample.ProviderPressure || oomUntil > now
            || cpuHighSince is { } since && now - since >= TimeSpan.FromSeconds(options.CpuPressureWindowSeconds)
            || memoryRatio < options.MemoryLowRatio || pidsRatio >= options.PidsHighRatio;
        if (pressure)
        {
            blocked = true;
            healthySamples = 0;
        }
        else if (blocked)
        {
            var healthy = sample.CpuUsageRatio < options.CpuRecoveryRatio
                && memoryRatio > options.MemoryRecoveryRatio && pidsRatio < options.PidsRecoveryRatio;
            if (!healthy) healthySamples = 0;
            else if (lastHealthyAt != sample.ObservedAt) healthySamples++;
            if (healthySamples >= options.HealthySamplesToResume) blocked = false;
        }
        lastHealthyAt = sample.ObservedAt;
        return blocked
            ? new(RunnerAdmissionState.PressureBlocked, RunnerAdmissionFailure.NodePressureHigh, sample)
            : new(RunnerAdmissionState.Ready, null, sample);
    }
}
