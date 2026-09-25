using System.Text.Json.Serialization;

namespace NoCTF.Domain.Runtime;

public enum RuntimeWorkloadKind : short
{
    Runtime,
    Compose,
    VerificationTarget,
    AwdChecker,
    PatchChecker
}

public enum RunnerAdmissionState : short
{
    Starting,
    Reconciling,
    Ready,
    PressureBlocked,
    ProviderUnavailable,
    Draining
}

public enum RunnerAdmissionFailure : short
{
    NoEligibleRunner,
    CpuActualCapacityInsufficient,
    MemoryActualCapacityInsufficient,
    PidActualCapacityInsufficient,
    NodePressureHigh,
    ObservationStale,
    LedgerRecovering,
    StartupConcurrencyLimited,
    ProviderUnavailable,
    RequestExceedsNodeCapacity
}

public readonly record struct RuntimeWorkloadIdentity(
    RuntimeWorkloadKind Kind,
    Guid RuntimeInstanceId,
    Guid OperationId)
{
    [JsonIgnore]
    public bool IsAuxiliary => Kind is RuntimeWorkloadKind.AwdChecker or RuntimeWorkloadKind.PatchChecker;

    // Text is used only at the Redis/provider metadata boundary.
    [JsonIgnore]
    public string Key => $"{(short)Kind}:{RuntimeInstanceId:N}:{OperationId:N}";

    public void Validate()
    {
        if (!Enum.IsDefined(Kind) || RuntimeInstanceId == Guid.Empty || OperationId == Guid.Empty)
            throw new InvalidOperationException("A capacity allocation requires a defined workload identity.");
        if (!IsAuxiliary && OperationId != RuntimeInstanceId)
            throw new InvalidOperationException("A primary allocation uses its Runtime UUID as operation identity.");
    }
}

public sealed record RuntimeResourceAmount(long MemoryBytes, long NanoCpus, long PidsLimit)
{
    public void Validate()
    {
        if (MemoryBytes <= 0 || NanoCpus <= 0 || PidsLimit <= 0)
            throw new InvalidOperationException("Allocation resource amounts must be positive.");
    }
}

public sealed record RuntimeCapacityAllocation(
    RuntimeWorkloadIdentity Identity,
    Guid? GameplayFactId,
    string ResourceDomain,
    string RunnerId,
    RuntimeResourceAmount Budget,
    RuntimeResourceAmount Limit)
{
    public void Validate()
    {
        Identity.Validate();
        ArgumentException.ThrowIfNullOrWhiteSpace(ResourceDomain);
        ArgumentException.ThrowIfNullOrWhiteSpace(RunnerId);
        if (ResourceDomain.Length > 256 || RunnerId.Length > 128
            || Identity.IsAuxiliary && GameplayFactId is null
            || GameplayFactId == Guid.Empty)
            throw new InvalidOperationException("Invalid capacity allocation owner or fact identity.");
        Budget.Validate();
        Limit.Validate();
        if (Budget.MemoryBytes != Limit.MemoryBytes || Budget.PidsLimit != Limit.PidsLimit
            || Budget.NanoCpus > Limit.NanoCpus)
            throw new InvalidOperationException("Only CPU may use a budget below its workload limit.");
    }
}

/// <summary>Only allocations whose resources may still exist; never an operation history.</summary>
public sealed record RuntimeCapacityAllocations(IReadOnlyList<RuntimeCapacityAllocation> Items)
{
    public const int MaximumItems = 32;
    public static RuntimeCapacityAllocations Empty { get; } = new(Array.Empty<RuntimeCapacityAllocation>());

    public RuntimeCapacityAllocations Add(RuntimeCapacityAllocation allocation)
    {
        allocation.Validate();
        var existing = Items.SingleOrDefault(item => item.Identity == allocation.Identity);
        if (existing is not null)
        {
            if (existing != allocation)
                throw new InvalidOperationException("An existing allocation cannot change owner or resource amounts.");
            return this;
        }
        var next = new RuntimeCapacityAllocations([.. Items, allocation]);
        next.Validate();
        return next;
    }

    public RuntimeCapacityAllocations Remove(RuntimeWorkloadIdentity identity) =>
        new(Items.Where(item => item.Identity != identity).ToArray());

    public void Validate()
    {
        if (Items is null || Items.Count > MaximumItems)
            throw new InvalidOperationException("Capacity allocations are missing or oversized.");
        foreach (var item in Items)
            item.Validate();
        if (Items.Select(item => item.Identity).Distinct().Count() != Items.Count
            || Items.Select(item => item.Identity.RuntimeInstanceId).Distinct().Count() > 1
            || Items.Count(item => !item.Identity.IsAuxiliary) > 1)
            throw new InvalidOperationException("Capacity allocations must have unique identities and one Runtime owner.");
    }

}
