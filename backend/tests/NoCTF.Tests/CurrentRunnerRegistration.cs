using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Runtime.Capacity;

namespace NoCTF.Tests;

internal static class CurrentRunnerRegistration
{
    public static RunnerAvailabilityRegistration Create(
        string pool,
        string runnerId,
        RuntimeResourceAmount capacity,
        bool hasActiveAssignments = false,
        bool providerAvailable = true,
        TimeSpan? timeToLive = null,
        ulong resourceDomainFencingToken = 0)
    {
        var now = DateTimeOffset.UtcNow;
        var options = new RunnerAdmissionOptions();
        var observed = new RunnerResourceObservation(
            runnerId,
            now,
            capacity.MemoryBytes,
            capacity.MemoryBytes,
            capacity.NanoCpus,
            0,
            0,
            capacity.PidsLimit,
            0);
        var amount = new RunnerObservedResourceAmount(
            capacity.MemoryBytes,
            capacity.NanoCpus,
            capacity.PidsLimit);
        var zero = new RunnerObservedResourceAmount(0, 0, 0);
        var snapshot = new RunnerAdmissionSnapshot(
            RunnerAdmissionState.Ready,
            null,
            observed,
            new(amount, amount, zero, amount));
        return new(
            pool,
            runnerId,
            RuntimeProvider.Docker,
            "current-test",
            timeToLive ?? TimeSpan.FromMinutes(1),
            hasActiveAssignments,
            providerAvailable,
            snapshot,
            options,
            Reconciled: true,
            ResourceDomainFencingToken: resourceDomainFencingToken);
    }
}
