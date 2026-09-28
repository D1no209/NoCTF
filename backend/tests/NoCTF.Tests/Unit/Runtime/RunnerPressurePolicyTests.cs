using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class RunnerPressurePolicyTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);
    private static RunnerResourceObservation Sample(int seconds, double cpu = .1, long available = 90, long oom = 0) =>
        new("test-domain", Start.AddSeconds(seconds), 100, available, 1_000_000_000, cpu, 10, 100, oom);

    [Test]
    public async Task Cpu_pressure_requires_a_window_and_three_distinct_healthy_samples()
    {
        var policy = new RunnerPressurePolicy(new());
        await Assert.That(policy.Evaluate(Sample(0, .95), Start).State).IsEqualTo(RunnerAdmissionState.Ready);
        await Assert.That(policy.Evaluate(Sample(19, .95), Start.AddSeconds(19)).State).IsEqualTo(RunnerAdmissionState.Ready);
        await Assert.That(policy.Evaluate(Sample(20, .95), Start.AddSeconds(20)).State).IsEqualTo(RunnerAdmissionState.PressureBlocked);
        await Assert.That(policy.Evaluate(Sample(25), Start.AddSeconds(25)).State).IsEqualTo(RunnerAdmissionState.PressureBlocked);
        await Assert.That(policy.Evaluate(Sample(25), Start.AddSeconds(26)).State).IsEqualTo(RunnerAdmissionState.PressureBlocked);
        await Assert.That(policy.Evaluate(Sample(30), Start.AddSeconds(30)).State).IsEqualTo(RunnerAdmissionState.PressureBlocked);
        await Assert.That(policy.Evaluate(Sample(35), Start.AddSeconds(35)).State).IsEqualTo(RunnerAdmissionState.Ready);
    }

    [Test]
    public async Task Stale_memory_and_oom_observations_block_without_changing_resource_amounts()
    {
        var policy = new RunnerPressurePolicy(new());
        await Assert.That(policy.Evaluate(Sample(0), Start.AddSeconds(16)).Failure).IsEqualTo(RunnerAdmissionFailure.ObservationStale);
        await Assert.That(policy.Evaluate(Sample(20, available: 5), Start.AddSeconds(20)).State).IsEqualTo(RunnerAdmissionState.PressureBlocked);
        var oom = policy.Evaluate(Sample(25, oom: 1), Start.AddSeconds(25));
        await Assert.That(oom.State).IsEqualTo(RunnerAdmissionState.PressureBlocked);
        await Assert.That(oom.Observation!.MemoryAvailableBytes).IsEqualTo(90);
        await Assert.That(policy.Evaluate(Sample(80, oom: 1), Start.AddSeconds(80)).State).IsEqualTo(RunnerAdmissionState.PressureBlocked);
        policy.Evaluate(Sample(85, oom: 1), Start.AddSeconds(85));
        policy.Evaluate(Sample(90, oom: 1), Start.AddSeconds(90));
        await Assert.That(policy.Evaluate(Sample(95, oom: 1), Start.AddSeconds(95)).State).IsEqualTo(RunnerAdmissionState.Ready);
    }

    [Test]
    public async Task Missing_pid_measurement_requires_an_explicit_provider_pressure_condition()
    {
        var policy = new RunnerPressurePolicy(new());
        var sample = Sample(0) with { PidsUsed = null };
        await Assert.That(policy.Evaluate(sample, Start).Failure).IsEqualTo(RunnerAdmissionFailure.ObservationStale);
        await Assert.That(policy.Evaluate(sample with { PidPressureConditionAvailable = true }, Start).State)
            .IsEqualTo(RunnerAdmissionState.Ready);
        await Assert.That(policy.Evaluate(sample with { PidsCapacity = null, PidPressureConditionAvailable = true }, Start)
            .Capacity!.AdmissionAvailable.PidsLimit).IsNull();
    }

    [Test]
    public async Task Admission_capacity_uses_actual_free_resources_and_threshold_headroom()
    {
        var sample = new RunnerResourceObservation("production-equivalent", Start,
            8L * 1024 * 1024 * 1024, 7L * 1024 * 1024 * 1024,
            4_000_000_000, .05, 384, 4096, 0);
        var result = new RunnerPressurePolicy(new()).Evaluate(sample, Start);

        await Assert.That(result.Capacity!.ObservedTotal)
            .IsEqualTo(new RunnerObservedResourceAmount(8L * 1024 * 1024 * 1024, 4_000_000_000, 4096));
        await Assert.That(result.Capacity.ObservedAvailable.NanoCpus).IsEqualTo(3_800_000_000);
        await Assert.That(result.Capacity.SafetyHeadroom)
            .IsEqualTo(new RunnerObservedResourceAmount(858_993_460, 400_000_000, 410));
        await Assert.That(result.Capacity.AdmissionAvailable)
            .IsEqualTo(new RunnerObservedResourceAmount(6_657_199_308, 3_400_000_000, 3302));
    }

    [Test]
    public async Task Invalid_or_future_observations_are_never_projected()
    {
        var policy = new RunnerPressurePolicy(new());
        foreach (var sample in new[]
        {
            Sample(0) with { CpuUsageRatio = double.NaN },
            Sample(0) with { CpuUsageRatio = -0.1 },
            Sample(0) with { MemoryAvailableBytes = -1 },
            Sample(1)
        })
        {
            var result = policy.Evaluate(sample, Start);
            await Assert.That(result.Failure).IsEqualTo(RunnerAdmissionFailure.ObservationStale);
            await Assert.That(result.Capacity).IsNull();
        }
    }
}
