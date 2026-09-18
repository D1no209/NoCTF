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
    }
}
