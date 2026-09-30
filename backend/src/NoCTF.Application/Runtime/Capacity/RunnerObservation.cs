using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Capacity;

public sealed record RunnerResourceObservation(
    string ResourceDomain,
    DateTimeOffset ObservedAt,
    long MemoryTotalBytes,
    long MemoryAvailableBytes,
    long CpuMillicores,
    double CpuUsageRatio,
    long? PidsUsed,
    long? PidsCapacity,
    long OomKills,
    bool ProviderPressure = false,
    bool PidPressureConditionAvailable = false);

public sealed record RunnerObservedResourceAmount(long MemoryBytes, long CpuMillicores, long? PidsLimit);

public sealed record RunnerCapacityProjection(
    RunnerObservedResourceAmount ObservedTotal,
    RunnerObservedResourceAmount ObservedAvailable,
    RunnerObservedResourceAmount SafetyHeadroom,
    RunnerObservedResourceAmount AdmissionAvailable);

public sealed record RunnerAdmissionSnapshot(
    RunnerAdmissionState State,
    RunnerAdmissionFailure? Failure,
    RunnerResourceObservation? Observation,
    RunnerCapacityProjection? Capacity = null);

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
    public string HostProcRoot { get; set; } = "/host/proc";
    public string HostCgroupRoot { get; set; } = "/host/sys/fs/cgroup";
    public string HostIdentityPath { get; set; } = "/host/etc/machine-id";

    public bool IsValid() => SampleIntervalSeconds > 0 && FreshnessSeconds > SampleIntervalSeconds
        && CpuPressureWindowSeconds > 0 && HealthySamplesToResume > 0
        && MainStartupConcurrency > 0 && AuxiliaryConcurrency > 0
        && CpuHighRatio is > 0 and <= 1 && CpuRecoveryRatio > 0 && CpuRecoveryRatio < CpuHighRatio
        && MemoryLowRatio > 0 && MemoryRecoveryRatio > MemoryLowRatio && MemoryRecoveryRatio < 1
        && PidsHighRatio is > 0 and <= 1 && PidsRecoveryRatio > 0 && PidsRecoveryRatio < PidsHighRatio
        && !string.IsNullOrWhiteSpace(HostProcRoot) && !string.IsNullOrWhiteSpace(HostCgroupRoot)
        && !string.IsNullOrWhiteSpace(HostIdentityPath);
}

/// <summary>Projects actual resource-domain headroom and gates new admission when observations are unsafe.</summary>
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
            || sample.ObservedAt > now || sample.MemoryTotalBytes <= 0 || sample.CpuMillicores <= 0
            || sample.MemoryAvailableBytes < 0 || !double.IsFinite(sample.CpuUsageRatio)
            || sample.CpuUsageRatio is < 0 or > 1
            || sample.PidsCapacity is <= 0 || sample.PidsUsed is < 0)
        {
            healthySamples = 0;
            return new(RunnerAdmissionState.Starting, RunnerAdmissionFailure.ObservationStale, sample);
        }
        var capacity = Project(sample);
        if (previousOomKills is long previous && sample.OomKills > previous)
            oomUntil = now.AddMinutes(1);
        previousOomKills = sample.OomKills;
        cpuHighSince = sample.CpuUsageRatio >= options.CpuHighRatio ? cpuHighSince ?? now : null;
        var memoryRatio = (double)sample.MemoryAvailableBytes / sample.MemoryTotalBytes;
        if (sample.PidsUsed is null && !sample.PidPressureConditionAvailable)
            return new(RunnerAdmissionState.Starting, RunnerAdmissionFailure.ObservationStale, sample);
        var pidsRatio = sample.PidsUsed is long pids && sample.PidsCapacity is long pidsCapacity
            ? (double)pids / pidsCapacity : 0;
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
            ? new(RunnerAdmissionState.PressureBlocked, RunnerAdmissionFailure.NodePressureHigh, sample, capacity)
            : new(RunnerAdmissionState.Ready, null, sample, capacity);
    }

    private RunnerCapacityProjection Project(RunnerResourceObservation sample)
    {
        var memoryTotal = sample.MemoryTotalBytes;
        var memoryAvailable = Math.Min(sample.MemoryAvailableBytes, memoryTotal);
        var cpuAvailable = ScaleFloor(sample.CpuMillicores, 1 - sample.CpuUsageRatio);
        var cpuHeadroom = ScaleCeiling(sample.CpuMillicores, 1 - options.CpuHighRatio);
        var memoryHeadroom = ScaleCeiling(memoryTotal, options.MemoryLowRatio);
        long? pidsTotal = sample.PidsUsed is not null ? sample.PidsCapacity : null;
        long? pidsAvailable = pidsTotal is long total && sample.PidsUsed is long used
            ? Math.Max(0, total - Math.Min(used, total)) : null;
        long? pidsHeadroom = pidsTotal is long pidCapacity
            ? ScaleCeiling(pidCapacity, 1 - options.PidsHighRatio) : null;
        return new(
            new(memoryTotal, sample.CpuMillicores, pidsTotal),
            new(memoryAvailable, cpuAvailable, pidsAvailable),
            new(memoryHeadroom, cpuHeadroom, pidsHeadroom),
            new(
                Math.Max(0, memoryAvailable - memoryHeadroom),
                Math.Max(0, cpuAvailable - cpuHeadroom),
                pidsAvailable is long available && pidsHeadroom is long reserved
                    ? Math.Max(0, available - reserved) : null));
    }

    private static long ScaleFloor(long value, double ratio) =>
        checked((long)decimal.Floor(value * (decimal)ratio));

    private static long ScaleCeiling(long value, double ratio) =>
        checked((long)decimal.Ceiling(value * (decimal)ratio));
}
